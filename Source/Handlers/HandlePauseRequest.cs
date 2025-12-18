using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override PauseResponse HandlePauseRequest(PauseArguments arguments)
    {
        Log.WriteLine("HandlePauseRequest");

        if (NoDebug) return new PauseResponse();

        RequestStop(StopReason_Pause.Instance);
        return new PauseResponse();
    }
}
