using System;
using System.Collections.Generic;
using LanguageCore;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    protected readonly Dictionary<int, Location> LocationReferences = [];

    protected override LocationsResponse HandleLocationsRequest(LocationsArguments arguments)
    {
        if (!LocationReferences.TryGetValue(arguments.LocationReference, out Location v))
        {
            throw new InvalidOperationException($"Location reference {arguments.LocationReference} not found");
        }

        return new LocationsResponse()
        {
            Column = ColumnToClient(v.Position.Range.Start.Character),
            Line = LineToClient(v.Position.Range.Start.Line),
            EndColumn = ColumnToClient(v.Position.Range.End.Character),
            EndLine = LineToClient(v.Position.Range.End.Line),
            Source = ToSource(v.File),
        };
    }
}
