using System;

namespace DebugServer;

static class Program
{
    static int Main(string[] args)
    {
        Logger? log = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--log")
            {
                if (++i < args.Length)
                {
                    log ??= new FileLogger(args[i]);
                }
            }
        }

        log ??= new VoidLogger();

        try
        {
            log.WriteLine("Started");

            BytecodeDebugAdapter adapter = new(Console.OpenStandardInput(), Console.OpenStandardOutput(), log);
            adapter.Protocol.LogMessage += (sender, e) => log.WriteLine(e.Message);
            adapter.Protocol.DispatcherError += (sender, e) => log.WriteLine(e.Exception.Message);

            adapter.Run();

            log.WriteLine("Waiting for reader");

            adapter.Protocol.WaitForReader();

            log.WriteLine("Exited");
            return 0;
        }
        catch (Exception ex)
        {
            log.WriteLine(ex);
            return 1;
        }
        finally
        {
            log.Dispose();
        }
    }
}
