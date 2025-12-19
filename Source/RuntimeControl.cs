using System;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    void Continue(StopReason? step)
    {
        using (SyncLock.EnterScope())
        {
            if (step is not null)
            {
                RequestStop(step);
            }
            else
            {
                StopReason = null;
                ShouldStop = false;
                //Log.WriteLine($"STOP REASON NULL");
            }
        }

        if (AllowProceedEvent.Set())
        {
            //Log.WriteLine($"CONTINUE PROCEED");
        }
        else
        {
            Log.WriteLine($"CONTINUE PROCEED failed");
        }
    }

    void RequestStop(StopReason reason)
    {
        using (SyncLock.EnterScope())
        {
            // When it is stopped, it's probably waiting for a proceed signal
            if (IsStopped)
            {
                // Reset the proceeded event so we can wait later
                DidProceedEvent.Set();

                // Allow the runtime to proceed
                AllowProceedEvent.Set();

                // Block the thread until the runtime is proceeded
                DidProceedEvent.WaitOne();
            }

            RequestStopUnsafe(reason);

            FlushStdout();
        }
    }

    void RequestStopUnsafe(StopReason reason)
    {
        if (NoDebug) throw new InvalidOperationException($"Cannot stop the runtime in no-debug mode");

        StopReason = reason;
        ShouldStop = true;
        //Log.WriteLine($"STOP REASON {reason}");

        if (AllowProceedEvent.Reset())
        {
            //Log.WriteLine($"CONTINUE BLOCK");
        }
        else
        {
            Log.WriteLine($"CONTINUE BLOCK failed");
        }
    }
}
