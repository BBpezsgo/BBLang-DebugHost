using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override NextResponse HandleNextRequest(NextArguments arguments)
    {
        Log.WriteLine("HandleNextRequest");

        Continue(step: true);
        return new NextResponse();
    }
}
