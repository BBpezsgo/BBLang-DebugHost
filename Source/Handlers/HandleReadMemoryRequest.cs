using System;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override ReadMemoryResponse HandleReadMemoryRequest(ReadMemoryArguments arguments)
    {
        if (!arguments.MemoryReference.StartsWith('m') || Processor is null)
        {
            return new ReadMemoryResponse();
        }
        else
        {
            int start = int.Parse(arguments.MemoryReference[1..]) + (arguments.Offset ?? 0);
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
