using System;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    protected override PauseResponse HandlePauseRequest(PauseArguments arguments)
    {
        Log.Trace($"[Handler] Pause");

        if (NoDebug) throw new InvalidOperationException($"Cannot handle request Pause in no-debug mode");

        RequestStop(StopReason_Pause.Instance);
        return new PauseResponse();
    }
}
