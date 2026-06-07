using System;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override WriteMemoryResponse HandleWriteMemoryRequest(WriteMemoryArguments arguments)
    {
        Log.Trace($"[Handler] WriteMemory");

        if (_processor is null || !int.TryParse(arguments.MemoryReference, out int address))
        {
            return new WriteMemoryResponse();
        }
        else
        {
            int start = address + (arguments.Offset ?? 0);
            if (start < 0 || start >= _processor.Memory.Length) return new WriteMemoryResponse();

            ReadOnlySpan<byte> data = Convert.FromBase64String(arguments.Data);
            Span<byte> destination = _processor.Memory.AsSpan(start);

            int writeLength = Math.Min(destination.Length, data.Length);

            data[..writeLength].CopyTo(destination);

            return new WriteMemoryResponse()
            {
                BytesWritten = writeLength,
                Offset = start,
            };
        }
    }
}
