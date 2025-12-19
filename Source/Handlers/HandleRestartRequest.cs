using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override RestartResponse HandleRestartRequest(RestartArguments arguments)
    {
        Log.WriteLine($"HandleRestartRequest");

        IsRestarting = true;

        Log.WriteLine($"restarting ...");

        Log.WriteLine($" allow runtime to proceed");
        AllowProceedEvent.Set();
        Log.WriteLine($" waiting for proceeding");
        DidProceedEvent.WaitOne();

        Log.WriteLine($" reset session");
        ResetSession();

        Log.WriteLine($" creating new processor");
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

        Log.WriteLine($" creating runtime thread");
        RuntimeThread = new(RuntimeImpl)
        {
            Name = "Runtime Thread"
        };
        Log.WriteLine($" starting runtime thread");
        RuntimeThread.Start();

        return new RestartResponse();
    }
}
