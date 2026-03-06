using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override DisconnectResponse HandleDisconnectRequest(DisconnectArguments arguments)
    {
        Log.Trace("[Handler] Disconnect");

        IsDisconnected = true;

        Continue(null);
        RuntimeThread?.Join();

        return new DisconnectResponse();
    }
}
