using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    protected override ScopesResponse HandleScopesRequest(ScopesArguments arguments)
    {
        Log.Trace($"[Handler] Scopes");

        if (!Processor.IsValid) return new ScopesResponse();

        using (SyncLock.EnterScope())
        {
            for (int i = 0; i < StackFrames.Count; i++)
            {
                FetchedFrame item = StackFrames[i];
                if (item.Id != arguments.FrameId) continue;
                List<Scope> result = new();
                if (i == 0)
                {
                    result.Add(new Scope()
                    {
                        Name = "Registers",
                        PresentationHint = Scope.PresentationHintValue.Registers,
                        VariablesReference = int.MaxValue,
                    });
                }
                foreach (FetchedScope scope in item.Scopes)
                {
                    (string name, Scope.PresentationHintValue presentationHint) = scope.Kind switch
                    {
                        FetchedScopeKind.ReturnValue => ("ReturnValue", Scope.PresentationHintValue.ReturnValue),
                        FetchedScopeKind.Locals => ("Locals", Scope.PresentationHintValue.Locals),
                        FetchedScopeKind.Arguments => ("Arguments", Scope.PresentationHintValue.Arguments),
                        FetchedScopeKind.Internals => ("Internals", Scope.PresentationHintValue.Unknown),
                        FetchedScopeKind.Globals => ("Globals", Scope.PresentationHintValue.Locals),
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
                        Source = ToSource(scope.Value.Location.Location.File),
                        VariablesReference = scope.Id,
                    });
                }
                return new ScopesResponse(result);
            }
        }

        return new ScopesResponse();
    }
}
