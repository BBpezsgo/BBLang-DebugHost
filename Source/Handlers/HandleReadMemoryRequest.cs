using System;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override ReadMemoryResponse HandleReadMemoryRequest(ReadMemoryArguments arguments)
    {
        if (Processor is null
            || !int.TryParse(arguments.MemoryReference, out int address))
        {
            return new ReadMemoryResponse();
        }
        else
        {
            int start = address + (arguments.Offset ?? 0);
            int length = arguments.Count;

            start = Math.Clamp(start, 0, Processor.Memory.Length - 1);
            length = Math.Clamp(length, 0, Processor.Memory.Length - start);

            Span<byte> memory = Processor.Memory.AsSpan(start, length);
            return new ReadMemoryResponse()
            {
                Address = start.ToString(),
                Data = Convert.ToBase64String(memory),
            };
        }
    }
}
