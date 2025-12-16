using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override ThreadsResponse HandleThreadsRequest(ThreadsArguments arguments)
    {
        Log.WriteLine("HandleThreadsRequest");

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
