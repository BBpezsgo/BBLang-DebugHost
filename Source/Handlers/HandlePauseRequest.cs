using System;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override PauseResponse HandlePauseRequest(PauseArguments arguments)
    {
        Log.WriteLine("HandlePauseRequest");

        if (NoDebug) throw new InvalidOperationException($"Cannot handle request Pause in no-debug mode");

        RequestStop(StopReason_Pause.Instance);
        return new PauseResponse();
    }
}
