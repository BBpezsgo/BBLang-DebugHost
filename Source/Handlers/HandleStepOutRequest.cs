using System;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override StepOutResponse HandleStepOutRequest(StepOutArguments arguments)
    {
        Log.WriteLine("HandleStepOutRequest");

        if (NoDebug) throw new InvalidOperationException($"Cannot handle request StepOut in no-debug mode");

        Continue(StopReason_StepOut.Instance);
        return new StepOutResponse();
    }
}
