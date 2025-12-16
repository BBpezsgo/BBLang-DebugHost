using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override SetDebuggerPropertyResponse HandleSetDebuggerPropertyRequest(SetDebuggerPropertyArguments arguments)
    {
        Log.WriteLine("HandleSetDebuggerPropertyRequest");

        return new SetDebuggerPropertyResponse();
    }
}
