using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override DisconnectResponse HandleDisconnectRequest(DisconnectArguments arguments)
    {
        Log.WriteLine("HandleDisconnectRequest");

        IsDisconnected = true;

        Continue(null);
        RuntimeThread?.Join();

        return new DisconnectResponse();
    }
}
