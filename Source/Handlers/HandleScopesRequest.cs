using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override ScopesResponse HandleScopesRequest(ScopesArguments arguments)
    {
        Log.WriteLine("HandleScopesRequest");

        if (Processor is null) return new ScopesResponse();

        using (SyncLock.EnterScope())
        {
            foreach (FetchedFrame item in StackFrames)
            {
                if (item.Id != arguments.FrameId) continue;
                List<Scope> result = [];
                foreach (FetchedScope scope in item.Scopes)
                {
                    (string name, Scope.PresentationHintValue presentationHint) = scope.Kind switch
                    {
                        FetchedScopeKind.ReturnValue => ("ReturnValue", Scope.PresentationHintValue.ReturnValue),
                        FetchedScopeKind.Locals => ("Locals", Scope.PresentationHintValue.Locals),
                        FetchedScopeKind.Arguments => ("Arguments", Scope.PresentationHintValue.Arguments),
                        FetchedScopeKind.Internals => ("Internals", default),
                        _ => throw new UnreachableException(),
                    };
                    result.Add(new Scope()
                    {
                        Line = LineToClient(scope.Value.Location.Location.Position.Range.Start.Line),
                        EndLine = LineToClient(scope.Value.Location.Location.Position.Range.End.Line),
                        Column = ColumnToClient(scope.Value.Location.Location.Position.Range.Start.Character),
                        EndColumn = ColumnToClient(scope.Value.Location.Location.Position.Range.End.Character),
                        NamedVariables = scope.Variables.Length,
                        Name = name,
                        PresentationHint = presentationHint,
                        Source = new Source()
                        {
                            Path = scope.Value.Location.Location.File.ToString(),
                            Name = Path.GetFileName(scope.Value.Location.Location.File.ToString()),
                        },
                        VariablesReference = scope.Id,
                    });
                }
                return new ScopesResponse(result);
            }
        }

        return new ScopesResponse();
    }
}
