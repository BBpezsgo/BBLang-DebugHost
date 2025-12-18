using System.Collections.Generic;
using System.IO;
using System.Linq;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override StackTraceResponse HandleStackTraceRequest(StackTraceArguments arguments)
    {
        Log.WriteLine("HandleStackTraceRequest");

        if (Processor is null) return new StackTraceResponse();

        using (SyncLock.EnterScope())
        {
            List<StackFrame> result = [];
            foreach (FetchedFrame frame in StackFrames)
            {
                string? functionName = frame.Function.IsValid ? (frame.Function.Identifier ?? frame.Function.Function?.ToReadable(frame.Function.TypeArguments) ?? "<unknown function>") : $"<{frame.Raw.InstructionPointer}>";
                if (Processor.DebugInformation.TryGetSourceLocation(frame.Raw.InstructionPointer, out SourceCodeLocation location))
                {
                    result.Add(new StackFrame()
                    {
                        Id = frame.Id,
                        Name = functionName ?? $"<{frame.Raw.InstructionPointer}>",
                        Line = LineToClient(location.Location.Position.Range.Start.Line),
                        EndLine = LineToClient(location.Location.Position.Range.End.Line),
                        Column = LineToClient(location.Location.Position.Range.Start.Character),
                        EndColumn = LineToClient(location.Location.Position.Range.End.Character),
                        Source = new Source()
                        {
                            Name = Path.GetFileName(location.Location.File.ToString()),
                            Path = location.Location.File.ToString(),
                        },
                        InstructionPointerReference = location.Instructions.Start.ToString(),
                    });
                }
                else if (frame.Function.IsValid && frame.Function.File is not null)
                {
                    result.Add(new StackFrame()
                    {
                        Id = frame.Id,
                        Name = functionName ?? $"<{frame.Raw.InstructionPointer}>",
                        Line = LineToClient(frame.Function.SourcePosition.Range.Start.Line),
                        EndLine = LineToClient(frame.Function.SourcePosition.Range.End.Line),
                        Column = LineToClient(frame.Function.SourcePosition.Range.Start.Character),
                        EndColumn = LineToClient(frame.Function.SourcePosition.Range.End.Character),
                        Source = frame.Function.File is null ? null : new Source()
                        {
                            Name = Path.GetFileName(frame.Function.File.ToString()),
                            Path = frame.Function.File.ToString(),
                        },
                        InstructionPointerReference = location.Instructions.Start.ToString(),
                    });
                }
                else
                {
                    result.Add(new StackFrame()
                    {
                        Id = frame.Id,
                        Name = functionName ?? $"<{frame.Raw.InstructionPointer}>",
                        InstructionPointerReference = frame.Raw.InstructionPointer.ToString(),
                    });
                }
            }
            return new StackTraceResponse(result);
        }
    }
}
