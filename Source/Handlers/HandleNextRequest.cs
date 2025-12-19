using System;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override NextResponse HandleNextRequest(NextArguments arguments)
    {
        Log.WriteLine("HandleNextRequest");

        if (NoDebug) throw new InvalidOperationException($"Cannot handle request Next in no-debug mode");

        Continue(StopReason_StepForward.Instance);
        return new NextResponse();
    }
}
