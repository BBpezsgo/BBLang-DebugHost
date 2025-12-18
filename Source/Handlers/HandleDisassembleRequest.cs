using System.Collections.Generic;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override DisassembleResponse HandleDisassembleRequest(DisassembleArguments arguments)
    {
        if (Processor is null
            || !int.TryParse(arguments.MemoryReference, out int address))
        {
            return new DisassembleResponse();
        }
        else
        {
            int start = address + (arguments.Offset ?? 0);
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
