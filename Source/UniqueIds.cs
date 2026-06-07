using System.Threading;

namespace DebugServer;

public struct UniqueIds
{
    int v;
    public int Next() => Interlocked.Increment(ref v);
}
