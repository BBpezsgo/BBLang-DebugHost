using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    protected override InitializeResponse HandleInitializeRequest(InitializeArguments arguments)
    {
        Log.Trace($"[Handler] Initialize");

        if (arguments.LinesStartAt1 == true) ClientsFirstLine = 1;
        if (arguments.ColumnsStartAt1 == true) ClientsFirstColumn = 1;

        Protocol.SendEvent(new InitializedEvent());

        return new InitializeResponse()
        {
            SupportsConfigurationDoneRequest = true,
            SupportsDebuggerProperties = true,
            SupportsBreakpointLocationsRequest = true,
            SupportsConditionalBreakpoints = true,
            SupportsLoadedSourcesRequest = true,
            SupportsLogPoints = true,
            SupportsCancelRequest = false,
            SupportsReadMemoryRequest = true,
            SupportsWriteMemoryRequest = true,
            SupportsDisassembleRequest = true,
            SupportsExceptionInfoRequest = true,
            SupportsRestartRequest = true,
            SupportsInstructionBreakpoints = true,
            SupportsCompletionsRequest = true,
            CompletionTriggerCharacters = new() { "." },
        };
    }
}
