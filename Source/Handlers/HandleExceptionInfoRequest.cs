using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override ExceptionInfoResponse HandleExceptionInfoRequest(ExceptionInfoArguments arguments)
    {
        if (arguments.ThreadId != 1 || Processor is null || CrashReason is null) return new ExceptionInfoResponse();

        return new ExceptionInfoResponse()
        {
            Description = CrashReason.Message,
            BreakMode = ExceptionBreakMode.Unhandled,
            Details = new ExceptionDetails()
            {
                TypeName = CrashReason.GetType().Name,
                Message = CrashReason.Message,
            },
        };
    }
}
