using System.Linq;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override ScopesResponse HandleScopesRequest(ScopesArguments arguments)
    {
        Log.WriteLine("HandleScopesRequest");

        if (Processor is null) return new ScopesResponse();

        using (SyncLock.EnterScope())
        {
            foreach (var item in StackFrames)
            {
                if (item.Frame.Id != arguments.FrameId) continue;
                return new ScopesResponse([.. item.Scopes.Select(v => v.Scope)]);
            }
        }

        return new ScopesResponse();
    }
}
