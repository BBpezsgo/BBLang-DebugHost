using System.Collections.Generic;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using StackFrame = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.StackFrame;

namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    protected override StackTraceResponse HandleStackTraceRequest(StackTraceArguments arguments)
    {
        Log.Trace($"[Handler] StackTrace");

        using (SyncLock.EnterScope())
        {
            List<StackFrame> result = new();
            foreach (FetchedFrame frame in StackFrames)
            {
                string functionName = frame.Function.ReadableIdentifier() ?? $"<{frame.Raw.InstructionPointer}>";

                if (DebugInformation.TryGetSourceLocation(frame.Raw.InstructionPointer, out SourceCodeLocation location, true))
                {
                    result.Add(new StackFrame()
                    {
                        Id = frame.Id,
                        Name = functionName,
                        Line = LineToClient(location.Location.Position.Range.Start.Line),
                        EndLine = LineToClient(location.Location.Position.Range.End.Line),
                        Column = LineToClient(location.Location.Position.Range.Start.Character),
                        EndColumn = LineToClient(location.Location.Position.Range.End.Character),
                        Source = ToSource(location.Location.File),
                        InstructionPointerReference = location.Instructions.Start.ToString(),
                        PresentationHint = StackFrame.PresentationHintValue.Normal,
                    });
                }
                else if (frame.Function.IsValid && frame.Function.File is not null)
                {
                    result.Add(new StackFrame()
                    {
                        Id = frame.Id,
                        Name = functionName,
                        Line = LineToClient(frame.Function.SourcePosition.Range.Start.Line),
                        EndLine = LineToClient(frame.Function.SourcePosition.Range.End.Line),
                        Column = LineToClient(frame.Function.SourcePosition.Range.Start.Character),
                        EndColumn = LineToClient(frame.Function.SourcePosition.Range.End.Character),
                        Source = frame.Function.File is null ? null : ToSource(frame.Function.File),
                        InstructionPointerReference = location.Instructions.Start.ToString(),
                        PresentationHint = StackFrame.PresentationHintValue.Normal,
                    });
                }
                else if (frame.Function.IsValid)
                {
                    result.Add(new StackFrame()
                    {
                        Id = frame.Id,
                        Name = functionName,
                        InstructionPointerReference = frame.Raw.InstructionPointer.ToString(),
                        PresentationHint = StackFrame.PresentationHintValue.Normal,
                    });
                }
                else
                {
                    result.Add(new StackFrame()
                    {
                        Id = frame.Id,
                        Name = functionName,
                        InstructionPointerReference = frame.Raw.InstructionPointer.ToString(),
                        PresentationHint = StackFrame.PresentationHintValue.Subtle,
                    });
                }
            }
            return new StackTraceResponse(result);
        }
    }
}
