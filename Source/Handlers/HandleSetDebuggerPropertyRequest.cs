using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override SetDebuggerPropertyResponse HandleSetDebuggerPropertyRequest(SetDebuggerPropertyArguments arguments)
    {
        Log.Trace($"[Handler] SetDebuggerProperty");

        return new SetDebuggerPropertyResponse();
    }
}
