using System;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    protected override NextResponse HandleNextRequest(NextArguments arguments)
    {
        Log.Trace($"[Handler] Next");

        if (NoDebug) throw new InvalidOperationException($"Cannot handle request Next in no-debug mode");

        Continue(arguments.Granularity == SteppingGranularity.Instruction ? StopReason_StepInstruction.Instance : StopReason_StepForward.Instance);
        return new NextResponse();
    }
}
