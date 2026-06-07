namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override void SendKey(byte c) => IO?.SendKey(c);
}
