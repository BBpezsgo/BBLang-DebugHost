using System.Text;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    readonly StringBuilder StdOut = new();
    int StdOutModifiedAt;

    void WriteStdout(char c)
    {
        StdOut.Append(c);
        StdOutModifiedAt = Time;
        if (c is '\r' or '\n')
        {
            FlushStdout();
        }
        else if (StdOut.Length > 10)
        {
            FlushStdout();
        }
    }

    void FlushStdout()
    {
        if (StdOut.Length > 0)
        {
            SendOutput(StdOut.ToString());
            StdOut.Clear();
        }
    }

    void SendOutput(string? message)
    {
        Protocol.SendEvent(new OutputEvent()
        {
            Category = OutputEvent.CategoryValue.Stdout,
            Output = string.IsNullOrEmpty(message) ? string.Empty : message.Trim(),
        });
    }
}
