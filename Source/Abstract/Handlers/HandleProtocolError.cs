using System;

namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    protected override void HandleProtocolError(Exception ex) => Log.Error(ex);
}
