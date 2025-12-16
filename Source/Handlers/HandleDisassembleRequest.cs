using System.Collections.Generic;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override DisassembleResponse HandleDisassembleRequest(DisassembleArguments arguments)
    {
        if (!arguments.MemoryReference.StartsWith('c') || Processor is null)
        {
            return new DisassembleResponse();
        }
        else
        {
            int start = int.Parse(arguments.MemoryReference[1..]) + (arguments.Offset ?? 0);
            int length = arguments.InstructionCount;

            List<DisassembledInstruction> result = [];

            for (int i = 0; i < length; i++)
            {
                int j = i + start;
                if (j < 0) continue;
                if (j >= Processor.Code.Length) break;
                Instruction c = Processor.Code[j];

                result.Add(new DisassembledInstruction()
                {
                    Address = j.ToString(),
                    Instruction = c.ToString(),
                });
            }

            return new DisassembleResponse(result);
        }
    }
}
