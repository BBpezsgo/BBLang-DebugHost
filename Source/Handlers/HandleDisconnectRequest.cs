using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override DisconnectResponse HandleDisconnectRequest(DisconnectArguments arguments)
    {
        Log.WriteLine("HandleDisconnectRequest");

        Continue(step: false);
        RuntimeThread?.Join();

        IsDisconnected = true;

        return new DisconnectResponse();
    }
}
