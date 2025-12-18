using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LanguageCore;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    bool TryGetBreakpointLocation(Uri file, SinglePosition position, out SourceCodeLocation result)
    {
        result = default;
        if (Processor is null) return false;

        SinglePosition pos = position;
        bool didFound = false;

        foreach (SourceCodeLocation item in Processor.DebugInformation.SourceCodeLocations)
        {
            if (item.Location.File != file) continue;
            if (item.Location.Position.Range.Start == pos)
            {
                Log.WriteLine($" ... {item.Location.Position.ToStringRange()} PERFECT");
                result = item;
                didFound = true;
                break;
            }
            if (item.Location.Position.Range.Start.Line != pos.Line) continue;
            if (!item.Location.Position.Range.Contains(pos)) continue;
            if (!didFound || item.Location.Position.AbsoluteRange.Size() < result.Location.Position.AbsoluteRange.Size())
            {
                Log.WriteLine($" ... {item.Location.Position.ToStringRange()}");
                result = item;
                didFound = true;
            }
        }

        if (didFound) return true;

        Log.WriteLine($" ... Fallback to first good position on line");
        foreach (SourceCodeLocation item in Processor.DebugInformation.SourceCodeLocations)
        {
            if (item.Location.File != file) continue;
            if (item.Location.Position.Range.Start.Line != pos.Line) continue;
            if (!didFound || item.Location.Position.Range.Start.Character < result.Location.Position.Range.Start.Character)
            {
                Log.WriteLine($" ... {item.Location.Position.ToStringRange()}");
                result = item;
                didFound = true;
            }
        }

        return didFound;
    }

    protected override SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments)
    {
        Log.WriteLine("HandleSetBreakpointsRequest");

        if (Processor is null) return new SetBreakpointsResponse([]);

        List<Breakpoint> result = [];

        List<Breakpoint> invalidBreakpoints = InvalidBreakpoints[ToUri(arguments.Source.Path)] = [];
        List<(Breakpoint Breakpoint, int Instruction, SourceBreakpoint SourceBreakpoint)> validBreakpoints = Breakpoints[ToUri(arguments.Source.Path)] = [];

        foreach (SourceBreakpoint breakpoint in arguments.Breakpoints)
        {
            SinglePosition pos = new(LineFromClient(breakpoint.Line), ColumnFromClient(breakpoint.Column ?? clientsFirstColumn));

            Log.WriteLine($"Trying to set breakpoint at {pos.ToStringMin()}");

            if (TryGetBreakpointLocation(ToUri(arguments.Source.Path), pos, out SourceCodeLocation selectedInstructions))
            {
                if (validBreakpoints.Any(v => v.Instruction == selectedInstructions.Instructions.Start))
                {
                    Log.WriteLine($"Duplicated breakpoint");
                    continue;
                }
                Breakpoint r = new()
                {
                    Id = BreakpointIds.Next(),
                    Line = LineToClient(selectedInstructions.Location.Position.Range.Start.Line),
                    Column = ColumnToClient(selectedInstructions.Location.Position.Range.Start.Character),
                    Verified = true,
                    Source = new Source()
                    {
                        Name = Path.GetFileName(selectedInstructions.Location.File.ToString()),
                        Path = selectedInstructions.Location.File.ToString(),
                    },
                    InstructionReference = selectedInstructions.Instructions.Start.ToString(),
                };
                result.Add(r);
                validBreakpoints.Add((r, selectedInstructions.Instructions.Start, breakpoint));
                Log.WriteLine($"BREAKPOINT {r.Line}:{r.Column} {selectedInstructions.Instructions.Start} {r.Source.Name}");
            }
            else
            {
                Breakpoint r = new()
                {
                    Id = BreakpointIds.Next(),
                    Line = breakpoint.Line,
                    Column = breakpoint.Column,
                    Message = $"Invalid location",
                    Verified = false,
                    Reason = Breakpoint.ReasonValue.Failed,
                };
                result.Add(r);
                invalidBreakpoints.Add(r);
                Log.WriteLine($"Cannot set breakpoint at {breakpoint.Line}:{breakpoint.Column}");
            }
        }

        return new SetBreakpointsResponse(result);
    }
}
