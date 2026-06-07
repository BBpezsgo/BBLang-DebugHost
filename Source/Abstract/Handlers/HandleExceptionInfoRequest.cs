using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    protected override ExceptionInfoResponse HandleExceptionInfoRequest(ExceptionInfoArguments arguments)
    {
        Log.Trace($"[Handler] ExceptionInfo");

        if (arguments.ThreadId != 1 || !Processor.IsValid || CrashReason is null) return new ExceptionInfoResponse();

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
