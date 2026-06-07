using System;

public abstract class Logger : IDisposable
{
    public abstract void WriteLine(string? value);
    public void WriteLine(object? value) => WriteLine(value?.ToString());

    public virtual void Trace(string? value) => WriteLine($"[trace] {value}");
    public void Trace(object? value) => Trace(value?.ToString());

    public virtual void Debug(string? value) => WriteLine($"[debug] {value}");
    public void Debug(object? value) => Debug(value?.ToString());

    public virtual void Info(string? value) => WriteLine($"[info] {value}");
    public void Info(object? value) => Info(value?.ToString());

    public virtual void Warn(string? value) => WriteLine($"[warn] {value}");
    public void Warn(object? value) => Warn(value?.ToString());

    public virtual void Error(string? value) => WriteLine($"[error] {value}");
    public void Error(object? value) => Error(value?.ToString());

    public abstract void Dispose();
}
