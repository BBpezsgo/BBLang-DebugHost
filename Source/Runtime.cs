using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
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
    StopReason? StopReason;
    RuntimeException? CrashReason;
    int Time;
    bool NoDebug;

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

        bool crashed = false;

        while (Processor is not null && !IsDisconnected && (!Processor.IsDone || (ShouldStop && StopReason is StopReason_Crash)))
        {
            if (ShouldStop)
            {
                using (SyncLock.EnterScope())
                {
                    List<CallTraceItem> stacktrace = [];
                    DebugUtils.TraceStack(Processor.Memory, Processor.Registers.BasePointer, Processor.DebugInformation.StackOffsets, stacktrace);

                    if (!Processor.DebugInformation.TryGetSourceLocation(Processor.Registers.CodePointer, out SourceCodeLocation sourceLocation))
                    {
                        goto _procceed;
                    }

                    FunctionInformation function = Processor.DebugInformation.GetFunctionInformation(Processor.Registers.CodePointer);

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
                DidProceedEvent.Set();
                Log.WriteLine("[#] Continued");

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

            if (crashed) break;

            try
            {
                Processor.Tick();
            }
            catch (RuntimeException ex)
            {
                RequestStopUnsafe(new StopReason_Crash()
                {
                    Exception = ex,
                });
                crashed = true;
                CrashReason = ex;
                continue;
            }

            if (!Processor.IsDone && StopReason is StopReason_StepForward or StopReason_StepIn or StopReason_StepOut)
            {
                RequestStopUnsafe(StopReason);
            }

            foreach (List<(Breakpoint Breakpoint, int Instruction, SourceBreakpoint SourceBreakpoint)> bps in Breakpoints.Values)
            {
                foreach ((Breakpoint breakpoint, int instruction, SourceBreakpoint sourceBreakpoint) in bps)
                {
                    if (instruction != Processor.Registers.CodePointer) continue;

                    Log.WriteLine($"BREAKPOINT HIT {sourceBreakpoint.Line}:{sourceBreakpoint.Column} at {instruction} in {breakpoint.Source.Name}");

                    RequestStopUnsafe(new StopReason_Breakpoint()
                    {
                        Breakpoint = breakpoint,
                    });
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
            Protocol.SendEvent(new ExitedEvent() { ExitCode = 0 });
            Protocol.SendEvent(new TerminatedEvent());
        }
        else
        {
            Protocol.SendEvent(new TerminatedEvent());
        }

        Processor = null;

        Log.WriteLine("[#] Exited");
    }

    void RuntimeImplNoDebug()
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

        bool crashed = false;

        while (Processor is not null && !IsDisconnected && !Processor.IsDone)
        {
            if (crashed) break;

            try
            {
                Processor.Tick();
            }
            catch (RuntimeException ex)
            {
                crashed = true;
                CrashReason = ex;
                break;
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
            if (crashed && CrashReason is not null)
            {
                Protocol.SendEvent(new OutputEvent()
                {
                    Output = CrashReason.ToString(),
                    Severity = OutputEvent.SeverityValue.Error,
                });
            }
            Protocol.SendEvent(new ExitedEvent() { ExitCode = 0 });
            Protocol.SendEvent(new TerminatedEvent());
        }
        else
        {
            Protocol.SendEvent(new TerminatedEvent());
        }

        Processor = null;

        Log.WriteLine("[#] Exited");
    }
}
