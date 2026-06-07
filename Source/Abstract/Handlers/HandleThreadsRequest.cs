using System.Collections.Generic;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    protected override ThreadsResponse HandleThreadsRequest(ThreadsArguments arguments)
    {
        Log.Trace($"[Handler] Threads");

        if (!Processor.IsValid)
        {
            return new ThreadsResponse()
            {
                Threads = new(),
            };
        }

        return new ThreadsResponse()
        {
            Threads = new List<Thread>()
            {
                new()
                {
                    Id = 1,
                    Name = "Main Thread",
                }
            },
        };
    }
}
