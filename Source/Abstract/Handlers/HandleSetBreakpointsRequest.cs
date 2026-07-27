using System;
using System.Collections.Generic;
using System.Linq;
using LanguageCore;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    bool TryGetBreakpointLocation(Uri file, SinglePosition position, out SourceCodeLocation result)
    {
        result = default;

        SinglePosition pos = position;
        bool didFound = false;

        foreach (SourceCodeLocation item in DebugInformation.SourceCodeLocations)
        {
            if (item.Location.File != file) continue;
            if (item.Location.Position.Range.Start == pos)
            {
                Log.Trace($" ... {item.Location.Position} PERFECT");
                result = item;
                didFound = true;
                break;
            }
            if (item.Location.Position.Range.Start.Line != pos.Line) continue;
            if (!item.Location.Position.Range.Contains(pos)) continue;
            if (!didFound || item.Location.Position.AbsoluteRange.Size() < result.Location.Position.AbsoluteRange.Size())
            {
                Log.Trace($" ... {item.Location.Position}");
                result = item;
                didFound = true;
            }
        }

        if (didFound) return true;

        const int lineRange = 5;

        Log.Trace($" ... Fallback to first good position in {lineRange} line range");
        foreach (SourceCodeLocation item in DebugInformation.SourceCodeLocations)
        {
            if (item.Location.File != file) continue;
            if (item.Location.Position.Range.Start.Line < pos.Line) continue;
            if (item.Location.Position.Range.Start.Line > pos.Line + lineRange) continue;
            if (!didFound || item.Location.Position.Range.Start < result.Location.Position.Range.Start)
            {
                Log.Trace($" ... {item.Location.Position}");
                result = item;
                didFound = true;
            }
        }

        return didFound;
    }

    protected override SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments)
    {
        Log.Trace($"[Handler] SetBreakpoints");

        List<Breakpoint> result = new();
        Uri uri = ToUri(arguments.Source);

        List<Breakpoint> invalidBreakpoints = _invalidBreakpoints[uri] = new();
        List<CompiledBreakpoint> validBreakpoints = _breakpoints[uri] = new();

        foreach (SourceBreakpoint breakpoint in arguments.Breakpoints)
        {
            SinglePosition pos = new(LineFromClient(breakpoint.Line), ColumnFromClient(breakpoint.Column ?? ClientsFirstColumn));

            Log.Trace($"Trying to set breakpoint at {uri} {pos.ToStringMin()}");

            if (TryGetBreakpointLocation(uri, pos, out SourceCodeLocation selectedInstructions))
            {
                if (validBreakpoints.Any(v => v.Instruction == selectedInstructions.Instructions.Start))
                {
                    Log.Info($"Duplicated breakpoint");
                    continue;
                }
                Breakpoint r = new()
                {
                    Id = BreakpointIds.Next(),
                    Line = LineToClient(selectedInstructions.Location.Position.Range.Start.Line),
                    Column = ColumnToClient(selectedInstructions.Location.Position.Range.Start.Character),
                    Verified = true,
                    Source = ToSource(selectedInstructions.Location.File),
                    InstructionReference = selectedInstructions.Instructions.Start.ToString(),
                };
                result.Add(r);
                validBreakpoints.Add(new CompiledBreakpoint(r, selectedInstructions.Instructions.Start, breakpoint, breakpoint.Condition, breakpoint.HitCondition, breakpoint.LogMessage));
                Log.Trace($"BREAKPOINT {r.Line}:{r.Column} {selectedInstructions.Instructions.Start} {r.Source.Path}");
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
                Log.Info($"Cannot set breakpoint at {uri} {breakpoint.Line}:{breakpoint.Column}");
            }
        }

        return new SetBreakpointsResponse(result);
    }
}
