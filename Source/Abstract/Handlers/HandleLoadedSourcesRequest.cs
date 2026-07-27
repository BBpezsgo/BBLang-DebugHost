using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    protected override LoadedSourcesResponse HandleLoadedSourcesRequest(LoadedSourcesArguments arguments)
    {
        Log.Trace($"[Handler] LoadedSources");

        List<Source> result = new();

        foreach ((_, Uri file) in Compiled.RawStatements)
        {
            result.Add(ToSource(file));
        }

        return new LoadedSourcesResponse(result);
    }
}
