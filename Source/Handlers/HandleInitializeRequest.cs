using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override InitializeResponse HandleInitializeRequest(InitializeArguments arguments)
    {
        Log.WriteLine("HandleInitializeRequest");

        if (arguments.LinesStartAt1 == true) clientsFirstLine = 1;
        if (arguments.ColumnsStartAt1 == true) clientsFirstColumn = 1;

        Protocol.SendEvent(new InitializedEvent());

        return new InitializeResponse()
        {
            SupportsConfigurationDoneRequest = true,
            SupportsDebuggerProperties = true,
            SupportsBreakpointLocationsRequest = true,
            SupportsLoadedSourcesRequest = true,
            SupportsLogPoints = true,
            SupportsCancelRequest = false,
            SupportsReadMemoryRequest = true,
            SupportsWriteMemoryRequest = true,
            SupportsDisassembleRequest = true,
            SupportsExceptionInfoRequest = true,
            SupportsInstructionBreakpoints = true,
        };
    }
}
