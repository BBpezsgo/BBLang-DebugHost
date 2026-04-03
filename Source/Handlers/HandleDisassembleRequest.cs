using System.Collections.Generic;
using System.IO;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override DisassembleResponse HandleDisassembleRequest(DisassembleArguments arguments)
    {
        Log.Trace("[Handler] Disassemble");

        if (Processor is null || !int.TryParse(arguments.MemoryReference, out int address))
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

                if (Processor.DebugInformation.TryGetSourceLocation(j, out SourceCodeLocation sourceLocation))
                {
                    result.Add(new DisassembledInstruction()
                    {
                        Address = j.ToString(),
                        Instruction = c.ToString(),
                        Line = LineToClient(sourceLocation.Location.Position.Range.Start.Line),
                        EndLine = LineToClient(sourceLocation.Location.Position.Range.End.Line),
                        Column = LineToClient(sourceLocation.Location.Position.Range.Start.Character),
                        EndColumn = LineToClient(sourceLocation.Location.Position.Range.End.Character),
                        Location = new Source()
                        {
                            Name = Path.GetFileName(sourceLocation.Location.File.ToString()),
                            Path = sourceLocation.Location.File.ToString(),
                        },
                    });
                }
                else
                {
                    result.Add(new DisassembledInstruction()
                    {
                        Address = j.ToString(),
                        Instruction = c.ToString(),
                    });
                }
            }

            return new DisassembleResponse(result);
        }
    }
}
