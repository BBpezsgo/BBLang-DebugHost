using System.Threading;

namespace DebugServer;

struct UniqueIds
{
    int v;
    public int Next() => Interlocked.Increment(ref v);
}
