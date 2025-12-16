using System;
using System.Threading;
using LanguageCore;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using SysThread = System.Threading.Thread;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    SysThread? RuntimeThread;
    readonly ManualResetEvent AllowProceedEvent;
    readonly ManualResetEvent DidProceedEvent;
    bool IsDisconnected;
    bool IsStopped;
    Location StopLocation;
    int StopBasePointer;
    bool ShouldStop;
    StopReason? StopReason;
    RuntimeException? CrashReason;
    int Time;

    void DebugThreadProc()
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

        while (Processor is not null && !IsDisconnected && (!Processor.IsDone || ShouldStop))
        {
            if (ShouldStop)
            {
                using (SyncLock.EnterScope())
                {
                    if (Processor.DebugInformation.TryGetSourceLocation(Processor.Registers.CodePointer, out SourceCodeLocation sourceLocation))
                    {
                        //log.WriteLine($"{sourceLocation.Location} == {stopLocation}");
                        if (sourceLocation.Location == StopLocation)
                        {
                            goto _procceed;
                        }

                        if (StopReason is StopReason_StepForward && StopLocation.Position.Range.Start.Line == sourceLocation.Location.Position.Range.Start.Line)
                        {
                            goto _procceed;
                        }
                    }

                    if (StopReason is StopReason_StepOut && StopBasePointer == Processor.Registers.BasePointer)
                    {
                        goto _procceed;
                    }

                    Log.WriteLine("[#] Stopped");
                    GatherInformation();
                    IsStopped = true;
                    StopLocation = sourceLocation.Location;

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

            if (StopReason is StopReason_StepForward or StopReason_StepIn or StopReason_StepOut)
            {
                RequestStopUnsafe(StopReason_StepForward.Instance);
            }

            foreach ((Breakpoint breakpoint, int instruction) in Breakpoints)
            {
                if (instruction != Processor.Registers.CodePointer) continue;

                RequestStopUnsafe(new StopReason_Breakpoint()
                {
                    Breakpoint = breakpoint,
                });
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

        Processor = null;

        Log.WriteLine("[#] Exited");
    }
}
