using System.IO;
using System.Threading;

class FileLogger : Logger
{
    readonly FileStream file;
    readonly StreamWriter stream;
    readonly Lock @lock;

    public FileLogger(string path)
    {
        File.WriteAllBytes(path, []);
        file = File.OpenWrite(path);
        stream = new StreamWriter(file);
        @lock = new();
    }

    public override void WriteLine(string? value)
    {
        using (@lock.EnterScope())
        {
            stream.WriteLine(value);
            stream.Flush();
        }
    }

    public override void Dispose()
    {
        using (@lock.EnterScope())
        {
            stream.Dispose();
            file.Dispose();
        }
    }
}
