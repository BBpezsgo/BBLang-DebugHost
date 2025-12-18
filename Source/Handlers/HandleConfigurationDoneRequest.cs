using System;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override ConfigurationDoneResponse HandleConfigurationDoneRequest(ConfigurationDoneArguments arguments)
    {
        Log.WriteLine("HandleConfigurationDoneRequest");
    
        if (RuntimeThread is null) throw new InvalidOperationException($"{nameof(RuntimeThread)} is null");
        RuntimeThread.Start();

        return new ConfigurationDoneResponse();
    }
}
