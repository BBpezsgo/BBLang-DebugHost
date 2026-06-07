using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LanguageCore;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    readonly AsciiBuilder StdOut = new();
    ArraySegment<CallTraceItem>? StdOutCommonTraceItem;
    int StdOutModifiedAt;

    void WriteStdout(byte c)
    {
        if (!Processor.IsValid) throw new UnreachableException();

        List<CallTraceItem> stacktrace = new();
        stacktrace.Add(new CallTraceItem(Processor.Registers.BasePointer, Processor.Registers.CodePointer));
        DebugUtils.TraceStack(Processor.Memory, Processor.Registers.BasePointer, DebugInformation.StackOffsets, stacktrace);
        stacktrace.Reverse();
        if (StdOutCommonTraceItem.HasValue)
        {
            int common = -1;
            for (int i = 0; i < Math.Min(StdOutCommonTraceItem.Value.Count, stacktrace.Count); i++)
            {
                if (StdOutCommonTraceItem.Value[i].InstructionPointer == stacktrace[i].InstructionPointer)
                {
                    common = i;
                }
                else
                {
                    break;
                }
            }
            StdOutCommonTraceItem = new(stacktrace.ToArray(), 0, common + 1);
        }
        else
        {
            StdOutCommonTraceItem = new(stacktrace.ToArray());
        }

        StdOut.Append(c);
        StdOutModifiedAt = Time;
        if (c is (byte)'\n')
        {
            FlushStdout();
        }
    }

    void FlushStdout()
    {
        if (StdOut.Length <= 0) return;

        if (Processor.IsValid && StdOutCommonTraceItem.HasValue && StdOutCommonTraceItem.Value.Count > 0 && DebugInformation.TryGetSourceLocation(StdOutCommonTraceItem.Value[^1].InstructionPointer, out SourceCodeLocation sourceLocation))
        {
            Protocol.SendEvent(new OutputEvent()
            {
                Category = OutputEvent.CategoryValue.Stdout,
                Output = StdOut.ToString(),
                Source = ToSource(sourceLocation.Location.File),
                Line = LineToClient(sourceLocation.Location.Position.Range.Start.Line),
                Column = ColumnToClient(sourceLocation.Location.Position.Range.Start.Character),
            });
        }
        else
        {
            Protocol.SendEvent(new OutputEvent()
            {
                Category = OutputEvent.CategoryValue.Stdout,
                Output = StdOut.ToString(),
            });
        }
        StdOut.Clear();
        StdOutCommonTraceItem = null;
    }
}
