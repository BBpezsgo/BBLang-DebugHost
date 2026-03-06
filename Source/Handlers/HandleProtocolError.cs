using System;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override void HandleProtocolError(Exception ex)
    {
        Log.Error(ex);
    }
}
