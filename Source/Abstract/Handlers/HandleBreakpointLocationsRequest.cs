using System.Collections.Generic;
using LanguageCore;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    protected override BreakpointLocationsResponse HandleBreakpointLocationsRequest(BreakpointLocationsArguments arguments)
    {
        Log.Trace($"[Handler] BreakpointLocations");

        List<BreakpointLocation> result = new();
        HashSet<SinglePosition> set = new();
        foreach (SourceCodeLocation item in DebugInformation.SourceCodeLocations)
        {
            if (item.Location.File != ToUri(arguments.Source)) continue;
            if (item.Location.Position.Range.Start.Line != LineFromClient(arguments.Line)) continue;
            if (!set.Add(item.Location.Position.Range.Start)) continue;
            result.Add(new BreakpointLocation()
            {
                Line = LineToClient(item.Location.Position.Range.Start.Line),
                EndLine = LineToClient(item.Location.Position.Range.End.Line),
                Column = LineToClient(item.Location.Position.Range.Start.Character),
                EndColumn = LineToClient(item.Location.Position.Range.End.Character),
            });
        }
        return new BreakpointLocationsResponse(result);
    }
}
