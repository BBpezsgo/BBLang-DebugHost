using System.Linq;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override StackTraceResponse HandleStackTraceRequest(StackTraceArguments arguments)
    {
        Log.WriteLine("HandleStackTraceRequest");

        if (Processor is null) return new StackTraceResponse();

        using (SyncLock.EnterScope())
        {
            return new StackTraceResponse([.. StackFrames.Select(v => v.Frame)]);
        }
    }
}
