using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override ContinueResponse HandleContinueRequest(ContinueArguments arguments)
    {
        Log.WriteLine("HandleContinueRequest");

        Continue(null);
        return new ContinueResponse();
    }
}
