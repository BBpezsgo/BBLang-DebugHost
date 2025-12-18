using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override StepInResponse HandleStepInRequest(StepInArguments arguments)
    {
        Log.WriteLine("HandleStepInRequest");

        Continue(StopReason_StepIn.Instance);
        return new StepInResponse();
    }
}
