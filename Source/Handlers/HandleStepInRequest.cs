using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override StepInResponse HandleStepInRequest(StepInArguments arguments)
    {
        Log.WriteLine("HandleStepInRequest");

        Continue(step: true);
        return new StepInResponse();
    }
}
