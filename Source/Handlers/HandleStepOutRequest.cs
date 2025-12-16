using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override StepOutResponse HandleStepOutRequest(StepOutArguments arguments)
    {
        Log.WriteLine("HandleStepOutRequest");

        Continue(step: true);
        return new StepOutResponse();
    }
}
