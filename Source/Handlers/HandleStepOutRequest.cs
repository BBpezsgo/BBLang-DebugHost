using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override StepOutResponse HandleStepOutRequest(StepOutArguments arguments)
    {
        Log.WriteLine("HandleStepOutRequest");

        Continue(StopReason_StepOut.Instance);
        return new StepOutResponse();
    }
}
