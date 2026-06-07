namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    protected abstract void Continue(StopReason? step);

    protected abstract void RequestStop(StopReason reason);
}
