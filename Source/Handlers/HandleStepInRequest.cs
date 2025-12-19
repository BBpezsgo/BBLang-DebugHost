using System;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override StepInResponse HandleStepInRequest(StepInArguments arguments)
    {
        Log.WriteLine("HandleStepInRequest");

        if (NoDebug) throw new InvalidOperationException($"Cannot handle request StepIn in no-debug mode");

        Continue(StopReason_StepIn.Instance);
        return new StepInResponse();
    }
}
