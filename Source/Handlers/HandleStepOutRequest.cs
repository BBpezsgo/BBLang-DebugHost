using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override StepOutResponse HandleStepOutRequest(StepOutArguments arguments)
    {
        Log.WriteLine("HandleStepOutRequest");

        if (NoDebug) return new StepOutResponse();

        Continue(StopReason_StepOut.Instance);
        return new StepOutResponse();
    }
}
