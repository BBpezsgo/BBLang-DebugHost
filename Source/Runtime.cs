using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using SysThread = System.Threading.Thread;

namespace DebugServer;

class StopContext
{
    public required int CodePointer;
    public required ImmutableArray<CallTraceItem> StackTrace;
    public required FunctionInformation Function;
    public required SourceCodeLocation Location;
}

partial class BytecodeDebugAdapter
{
    SysThread? RuntimeThread;
    readonly ManualResetEvent AllowProceedEvent;
    readonly ManualResetEvent DidProceedEvent;
    bool IsDisconnected;
    bool IsStopped;
    StopContext? LastStopContext;
    bool ShouldStop;
    bool IsRestarting;
    StopReason? StopReason;
    RuntimeException? CrashReason;
    int Time;
    bool NoDebug;
    bool StopOnEntry;

    void RuntimeImpl()
    {
        using (SyncLock.EnterScope())
        {
            Log.WriteLine("[#] Started");
            Protocol.SendEvent(new ContinuedEvent()
            {
                ThreadId = 1,
                AllThreadsContinued = true,
            });
        }

        while (Processor is not null && !IsDisconnected && (!Processor.IsDone || (ShouldStop && StopReason is StopReason_Crash)))
        {
            if (ShouldStop)
            {
                using (SyncLock.EnterScope())
                {
                    List<CallTraceItem> stacktrace = [];
                    DebugUtils.TraceStack(Processor.Memory, Processor.Registers.BasePointer, Processor.DebugInformation.StackOffsets, stacktrace);
                    FunctionInformation function = Processor.DebugInformation.GetFunctionInformation(Processor.Registers.CodePointer);
                    SourceCodeLocation sourceLocation = default;

                    if (StopReason is not StopReason_StepInstruction)
                    {
                        if (!Processor.DebugInformation.TryGetSourceLocation(Processor.Registers.CodePointer, out sourceLocation))
                        {
                            goto _procceed;
                        }

                        if (LastStopContext is not null)
                        {
                            if (sourceLocation.Location == LastStopContext.Location.Location)
                            {
                                goto _procceed;
                            }

                            if (StopReason is StopReason_StepForward && stacktrace.Count > LastStopContext.StackTrace.Length)
                            {
                                goto _procceed;
                            }

                            if (StopReason is StopReason_StepOut && stacktrace.Count >= LastStopContext.StackTrace.Length)
                            {
                                goto _procceed;
                            }
                        }
                    }

                    Log.WriteLine("[#] Stopped");
                    GatherInformation();
                    IsStopped = true;
                    LastStopContext = new StopContext()
                    {
                        CodePointer = Processor.Registers.CodePointer,
                        Function = function,
                        Location = sourceLocation,
                        StackTrace = [.. stacktrace],
                    };

                    switch (StopReason)
                    {
                        case null:
                            Log.WriteLine("[#] Stopped for no reason");
                            throw new InvalidOperationException("Stopped for no reason");
                        case StopReason_StepForward:
                        case StopReason_StepIn:
                        case StopReason_StepOut:
                        case StopReason_StepInstruction:
                            Protocol.SendEvent(new StoppedEvent()
                            {
                                Reason = StoppedEvent.ReasonValue.Step,
                                AllThreadsStopped = true,
                                ThreadId = 1,
                            });
                            break;
                        case StopReason_Pause:
                            Protocol.SendEvent(new StoppedEvent()
                            {
                                Reason = StoppedEvent.ReasonValue.Pause,
                                AllThreadsStopped = true,
                                ThreadId = 1,
                            });
                            break;
                        case StopReason_Crash:
                            Protocol.SendEvent(new StoppedEvent()
                            {
                                Reason = StoppedEvent.ReasonValue.Exception,
                                AllThreadsStopped = true,
                                ThreadId = 1,
                            });
                            break;
                        case StopReason_Breakpoint v:
                            Protocol.SendEvent(new StoppedEvent()
                            {
                                Reason = StoppedEvent.ReasonValue.Breakpoint,
                                AllThreadsStopped = true,
                                ThreadId = 1,
                                HitBreakpointIds = v.Breakpoint.Id.HasValue ? [v.Breakpoint.Id.Value] : [],
                            });
                            break;
                        default:
                            throw new NotImplementedException(StopReason.GetType().Name);
                    }
                }

                Log.WriteLine("[#] Waiting to continue ...");
                AllowProceedEvent.WaitOne();
                Log.WriteLine("[#] Continued");
                DidProceedEvent.Set();

                if (IsRestarting)
                {
                    Log.WriteLine("[#] Breaking runtime thread (restarting)");
                    break;
                }

                using (SyncLock.EnterScope())
                {
                    IsStopped = false;

                    Protocol.SendEvent(new ContinuedEvent()
                    {
                        ThreadId = 1,
                        AllThreadsContinued = true,
                    });
                }

            _procceed:;
            }

            if (CrashReason is not null) break;

            try
            {
                Processor.Tick();
            }
            catch (RuntimeException ex)
            {
                CrashReason = ex;
                if (NoDebug)
                {
                    break;
                }
                else
                {
                    RequestStopUnsafe(new StopReason_Crash()
                    {
                        Exception = ex,
                    });
                    continue;
                }
            }

            if (!Processor.IsDone && StopReason is StopReason_StepForward or StopReason_StepIn or StopReason_StepOut)
            {
                RequestStopUnsafe(StopReason);
            }

            if (!NoDebug)
            {
                foreach (var item in InstructionBreakpoints)
                {
                    if (item.Address != Processor.Registers.CodePointer) continue;

                    Log.WriteLine($"BREAKPOINT HIT at {item.Address}");

                    RequestStopUnsafe(new StopReason_Breakpoint()
                    {
                        Breakpoint = item.Breakpoint,
                    });
                }

                foreach (List<CompiledBreakpoint> bps in Breakpoints.Values)
                {
                    foreach (CompiledBreakpoint breakpoint in bps)
                    {
                        if (breakpoint.Instruction != Processor.Registers.CodePointer) continue;

                        Log.WriteLine($"BREAKPOINT HIT {breakpoint.SourceBreakpoint.Line}:{breakpoint.SourceBreakpoint.Column} at {breakpoint.Instruction} in {breakpoint.Breakpoint.Source.Name}");

                        using (SyncLock.EnterScope())
                        {
                            bool informationGathered = false;

                            if (!string.IsNullOrWhiteSpace(breakpoint.Condition))
                            {
                                DiagnosticsCollection diagnostics = new();

                                if (!informationGathered)
                                {
                                    GatherInformation();
                                    informationGathered = true;
                                }

                                if (TryEvaluate(breakpoint.Condition, StackFrames.Count > 0 ? StackFrames[0].Id : null, diagnostics, out bool result))
                                {
                                    if (!result) goto skip;
                                }
                                else
                                {
                                    StringBuilder b = new();
                                    b.AppendLine($"Failed to evaluate breakpoint condition `{breakpoint.Condition}` at {breakpoint.SourceBreakpoint.Line}:{breakpoint.SourceBreakpoint.Column} in {breakpoint.Breakpoint.Source.Name}");
                                    diagnostics.WriteErrorsTo(b);
                                    Protocol.SendEvent(new OutputEvent()
                                    {
                                        Output = b.ToString(),
                                        Severity = OutputEvent.SeverityValue.Error,
                                    });
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(breakpoint.LogMessage))
                            {
                                if (!informationGathered)
                                {
                                    GatherInformation();
                                    informationGathered = true;
                                }

                                List<ExpressionVariable> variables = StackFrames.Count > 0 ? GetExpressionVariables(StackFrames[0].Id) : [];
                                string template = breakpoint.LogMessage;
                                int i = 0;
                                StringBuilder res = new();
                                while (i < template.Length)
                                {
                                    int j = template.IndexOf('{', i);
                                    if (j != -1)
                                    {
                                        int k = template.IndexOf('}', j);
                                        if (k != -1)
                                        {
                                            res.Append(template[i..j]);

                                            string item = template[(j + 1)..k];

                                            if (variables.TryFind(v => v.Name == item, out ExpressionVariable variable))
                                            {
                                                UniqueIds uniqueIds = new();
                                                res.Append(ToVariable(variable.Address, variable.Type, Processor.Memory, variable.Name, ref uniqueIds).Value);
                                            }

                                            i = k + 1;
                                            continue;
                                        }
                                    }

                                    res.Append(template[i..]);
                                    break;
                                }
                                res.AppendLine();
                                Protocol.SendEvent(new OutputEvent()
                                {
                                    Output = res.ToString(),
                                    Category = OutputEvent.CategoryValue.Console,
                                    Source = breakpoint.Breakpoint.Source,
                                    Line = breakpoint.SourceBreakpoint.Line,
                                    Column = breakpoint.SourceBreakpoint.Column,
                                });
                                goto skip;
                            }

                            RequestStopUnsafe(new StopReason_Breakpoint()
                            {
                                Breakpoint = breakpoint.Breakpoint,
                            });

                        skip:;
                        }
                    }
                }
            }

            if (StdOutModifiedAt != 0 && Time - StdOutModifiedAt > 30)
            {
                FlushStdout();
                StdOutModifiedAt = 0;
            }

            SysThread.Yield();
        }

        if (!IsDisconnected)
        {
            FlushStdout();
            if (CrashReason is not null)
            {
                Protocol.SendEvent(new OutputEvent()
                {
                    Output = CrashReason.ToString(),
                    Category = OutputEvent.CategoryValue.Exception,
                    Severity = OutputEvent.SeverityValue.Error,
                });
            }

            if (!IsRestarting)
            {
                Protocol.SendEvent(new ExitedEvent() { ExitCode = 0 });
                Protocol.SendEvent(new TerminatedEvent());
            }
        }

        Processor = null;

        Log.WriteLine("[#] Exited");
    }
}
