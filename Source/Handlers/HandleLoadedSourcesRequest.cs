using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override LoadedSourcesResponse HandleLoadedSourcesRequest(LoadedSourcesArguments arguments)
    {
        Log.WriteLine("HandleLoadedSourcesRequest");

        List<Source> result = [];

        foreach ((_, Uri file) in Compiled.RawStatements)
        {
            result.Add(new Source()
            {
                Name = Path.GetFileName(file.ToString()),
                Path = file.ToString(),
            });
        }

        return new LoadedSourcesResponse(result);
    }
}
