using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override RestartResponse HandleRestartRequest(RestartArguments arguments)
    {
        Log.Trace($"[Handler] Restart");

        IsRestarting = true;

        Log.Trace($"restarting ...");

        Log.Trace($" allow runtime to proceed");
        AllowProceedEvent.Set();
        Log.Trace($" waiting for proceeding");
        DidProceedEvent.WaitOne();

        Log.Trace($" reset session");
        ResetSession();

        Log.Trace($" creating new processor");
        Processor = new BytecodeProcessor(
            BytecodeInterpreterSettings.Default,
            Generated.Code,
            null,
            Generated.DebugInfo,
            Compiled.ExternalFunctions,
            Generated.GeneratedUnmanagedFunctions
        );

        if (!NoDebug && StopOnEntry)
        {
            RequestStop(StopReason_Pause.Instance);
        }
        else
        {
            StopReason = null;
        }

        Log.Trace($" creating runtime thread");
        RuntimeThread = new(RuntimeImpl)
        {
            Name = "Runtime Thread"
        };
        Log.Trace($" starting runtime thread");
        RuntimeThread.Start();

        return new RestartResponse();
    }
}
