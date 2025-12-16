using System.Collections.Generic;
using LanguageCore;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override BreakpointLocationsResponse HandleBreakpointLocationsRequest(BreakpointLocationsArguments arguments)
    {
        Log.WriteLine("HandleBreakpointLocationsRequest");

        UnverifiedBreakpoints.Add(new Breakpoint()
        {
            Source = arguments.Source,
            Line = arguments.Line,
            Column = arguments.Column,
            EndColumn = arguments.EndColumn,
            EndLine = arguments.EndLine,
            Verified = false,
            Id = BreakpointIds.Next(),
        });

        if (Processor is null) return new BreakpointLocationsResponse();

        TrySetBreakpoints();

        List<BreakpointLocation> result = [];
        for (int i = 0; i < Breakpoints.Count; i++)
        {
            if (!Processor.DebugInformation.TryGetSourceLocation(Breakpoints[i].Instruction, out SourceCodeLocation location)) continue;
            if (location.Location.File != ToUri(arguments.Source.Path)) continue;

            result.Add(new BreakpointLocation()
            {
                Column = ColumnToClient(location.Location.Position.Range.Start.Character),
                EndColumn = ColumnToClient(location.Location.Position.Range.End.Character),
                Line = LineToClient(location.Location.Position.Range.Start.Line),
                EndLine = LineToClient(location.Location.Position.Range.End.Line),
            });
        }
        return new BreakpointLocationsResponse();
    }
}
