using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override ThreadsResponse HandleThreadsRequest(ThreadsArguments arguments)
    {
        Log.Trace($"[Handler] Threads");

        if (Processor is null)
        {
            return new ThreadsResponse()
            {
                Threads = [],
            };
        }

        return new ThreadsResponse()
        {
            Threads =
            [
                new Thread()
                {
                    Id = 1,
                    Name = "Main Thread",
                }
            ],
        };
    }
}
