using System;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    protected override ReadMemoryResponse HandleReadMemoryRequest(ReadMemoryArguments arguments)
    {
        Log.Trace($"[Handler] ReadMemory");

        if (!Processor.IsValid || !int.TryParse(arguments.MemoryReference, out int address))
        {
            return new ReadMemoryResponse();
        }

        int start = address + (arguments.Offset ?? 0);
        int length = arguments.Count;

        start = Math.Clamp(start, 0, Processor.Memory.Length - 1);
        length = Math.Clamp(length, 0, Processor.Memory.Length - start);

        ReadOnlySpan<byte> memory = Processor.Memory.Slice(start, length);
        return new ReadMemoryResponse()
        {
            Address = start.ToString(),
            Data = Convert.ToBase64String(memory),
        };
    }
}
