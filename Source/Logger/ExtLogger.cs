using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;

namespace DebugServer;

class ExtLogger(DebugProtocolClient protocol) : Logger
{
    void Send(string message, string? level)
    {
        if (!protocol.IsRunning) return;
        protocol.SendEvent(new DebugLogEvent()
        {
            Message = message ?? string.Empty,
            Level = null,
        });
    }

    public override void WriteLine(string? value) => Send(value ?? string.Empty, null);
    public override void Trace(string? value) => Send(value ?? string.Empty, "trace");
    public override void Debug(string? value) => Send(value ?? string.Empty, "debug");
    public override void Info(string? value) => Send(value ?? string.Empty, "info");
    public override void Warn(string? value) => Send(value ?? string.Empty, "warn");
    public override void Error(string? value) => Send(value ?? string.Empty, "error");

    public override void Dispose() { }
}
