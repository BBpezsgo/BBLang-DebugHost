using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using StackFrame = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.StackFrame;
using SysThread = System.Threading.Thread;

namespace DebugServer;

partial class BytecodeDebugAdapter : DebugAdapterBase
{
    readonly Lock SyncLock = new();
    UniqueIds BreakpointIds;

    readonly Logger Log;

    CompilerResult Compiled;
    BBLangGeneratorResult Generated;
    BytecodeProcessor? Processor;

    readonly List<Breakpoint> UnverifiedBreakpoints = [];
    readonly List<Breakpoint> InvalidBreakpoints = [];
    readonly List<(Breakpoint Breakpoint, int Instruction)> Breakpoints = [];

    readonly List<(StackFrame Frame, List<(Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Scope Scope, List<(Variable Variable, int Id)> Variables)> Scopes)> StackFrames = [];
    readonly List<(GeneralType Type, int Id, int Address, string ParentName)> IndirectVariables = [];
    UniqueIds CurrentUniqueIds;

    public BytecodeDebugAdapter(Stream stdIn, Stream stdOut, Logger log)
    {
        AllowProceedEvent = new ManualResetEvent(true);
        DidProceedEvent = new ManualResetEvent(false);
        InitializeProtocolClient(stdIn, stdOut);
        Log = log;
    }

    Variable ToVariable(int address, GeneralType type, ReadOnlySpan<byte> memory, string name, ref UniqueIds ids)
    {
        if (!StatementCompiler.FindSize(type, out int size, out _, new RuntimeInfoProvider() { PointerSize = CodeGeneratorForMain.DefaultCompilerSettings.PointerSize }))
        {
            return new Variable()
            {
                Type = type.ToString(),
                Value = "?",
            };
        }
        else
        {
            return ToVariable(new Range<int>(address, address + size), type, memory, name, ref ids);
        }
    }

    Variable ToVariable(Range<int> address, GeneralType type, ReadOnlySpan<byte> memory, string name, ref UniqueIds ids)
    {
        Variable variable = new()
        {
            Type = type.ToString(),
            Name = name,
            Value = "?",
        };

        if (address.Start < 0 || address.End >= memory.Length)
        {
            variable.Value = "<invalid>";
        }
        else
        {
            switch (type.FinalValue)
            {
                case BuiltinType v:
                    variable.Value = v.Type switch
                    {
                        BasicType.Void => "void",
                        BasicType.Any => "any",
                        BasicType.U8 => memory.Get<byte>(address.Start).ToString(),
                        BasicType.I8 => memory.Get<sbyte>(address.Start).ToString(),
                        BasicType.U16 => memory.Get<ushort>(address.Start).ToString(),
                        BasicType.I16 => memory.Get<short>(address.Start).ToString(),
                        BasicType.U32 => memory.Get<uint>(address.Start).ToString(),
                        BasicType.I32 => memory.Get<int>(address.Start).ToString(),
                        BasicType.U64 => memory.Get<ulong>(address.Start).ToString(),
                        BasicType.I64 => memory.Get<long>(address.Start).ToString(),
                        BasicType.F32 => memory.Get<float>(address.Start).ToString(),
                        _ => throw new UnreachableException(),
                    };
                    break;
                case PointerType v:
                {
                    int pointerValue = memory.Get<int>(address.Start);
                    variable.Value = $"0x{Convert.ToString(pointerValue, 16)}";
                    variable.MemoryReference = $"m{pointerValue}";
                    variable.VariablesReference = DiscoverIndirectVariables(pointerValue, v.To, memory, name, ref ids);
                    break;
                }
                case ArrayType v:
                {
                    if (v.Length.HasValue && StatementCompiler.FindSize(v.Of, out _, out _, new RuntimeInfoProvider() { PointerSize = MainGeneratorSettings.Default.PointerSize }))
                    {
                        variable.VariablesReference = DiscoverIndirectVariables(address.Start, v, memory, name, ref ids);
                    }
                    else
                    {
                        variable.Value = "[ ? ]";
                    }
                    break;
                }
                case StructType v:
                {
                    variable.Value = "{ ... }";
                    variable.VariablesReference = DiscoverIndirectVariables(address.Start, v, memory, name, ref ids);
                    break;
                }
                case FunctionType v:
                {
                    int pointerValue = memory.Get<int>(address.Start);
                    variable.Value = $"0x{Convert.ToString(pointerValue, 16)}";
                    variable.MemoryReference = $"c{pointerValue}";
                    break;
                }
                case GenericType:
                case AliasType:
                default:
                    throw new UnreachableException();
            }
        }

        return variable;
    }

    int DiscoverIndirectVariables(int pointerValue, GeneralType type, ReadOnlySpan<byte> memory, string parentName, ref UniqueIds ids)
    {
        if (pointerValue > 0 && pointerValue < memory.Length)
        {
            foreach (var indirectVariable in IndirectVariables)
            {
                if (indirectVariable.Address != pointerValue) continue;
                return indirectVariable.Id;
            }
            int id = ids.Next();
            IndirectVariables.Add((type, id, pointerValue, parentName));
            return id;
        }
        else
        {
            return 0;
        }
    }

    void GatherInformation()
    {
        StackFrames.Clear();
        IndirectVariables.Clear();
        CurrentUniqueIds = new UniqueIds();

        if (Processor is null) return;

        IReadOnlyList<CallTraceItem> trace;

        {
            List<CallTraceItem> _trace = [];
            DebugUtils.TraceStack(Processor.Memory, Processor.Registers.BasePointer, Processor.DebugInformation.StackOffsets, _trace);
            _trace.Insert(0, new CallTraceItem(Processor.Registers.BasePointer, Processor.Registers.CodePointer));
            trace = _trace;
        }

        ReadOnlySpan<byte> memory = Processor.Memory;

        foreach (CallTraceItem frame in trace)
        {
            if (frame.InstructionPointer < 0 || frame.InstructionPointer >= Processor.Code.Length) continue;

            FunctionInformation f = Processor.DebugInformation.GetFunctionInformation(frame.InstructionPointer);
            int frameId = CurrentUniqueIds.Next();
            string? functionName = f.IsValid ? (f.Identifier ?? f.Function?.ToReadable(f.TypeArguments) ?? "<unknown function>") : null;

            StackFrame stackFrame;
            if (Processor.DebugInformation.TryGetSourceLocation(frame.InstructionPointer, out SourceCodeLocation location))
            {
                stackFrame = new StackFrame()
                {
                    Line = LineToClient(location.Location.Position.Range.Start.Line),
                    EndLine = LineToClient(location.Location.Position.Range.End.Line),
                    Column = LineToClient(location.Location.Position.Range.Start.Character),
                    EndColumn = LineToClient(location.Location.Position.Range.End.Character),
                    Name = functionName ?? $"<{frame.InstructionPointer}>",
                    Source = new Source()
                    {
                        Name = Path.GetFileName(location.Location.File.ToString()),
                        Path = location.Location.File.ToString(),
                    },
                    Id = frameId,
                };
            }
            else
            {
                stackFrame = new StackFrame()
                {
                    Name = functionName ?? $"<{frame.InstructionPointer}>",
                    Id = frameId,
                };
            }

            List<(Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Scope Scope, List<(Variable Variable, int Id)> Variables)> frameScopes = [];
            ImmutableArray<ScopeInformation> _scopes = Processor.DebugInformation.GetScopes(frame.InstructionPointer);

            foreach (ScopeInformation scope in _scopes)
            {
                List<(Variable Variable, int Id)> locals = [];
                List<(Variable Variable, int Id)> arguments = [];
                List<(Variable Variable, int Id)> internals = [];
                List<(Variable Variable, int Id)> returnValue = [];

                foreach (StackElementInformation item in scope.Stack)
                {
                    Log.WriteLine(item.Identifier);

                    Range<int> address = item.GetRange(Processor.Registers.BasePointer, Processor.StackStart);
                    Variable variable = ToVariable(address, item.Type, memory, item.Identifier, ref CurrentUniqueIds);
                    variable.EvaluateName = item.Identifier;

                    (item.Kind switch
                    {
                        StackElementKind.Internal => item.Identifier == "Return Value" ? returnValue : internals,
                        StackElementKind.Variable => locals,
                        StackElementKind.Parameter => arguments,
                        _ => throw new UnreachableException(),
                    }).Add((variable, CurrentUniqueIds.Next()));
                }

                /*
                if (internals.Count > 0)
                {
                    frameScopes.Add((new Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Scope()
                    {
                        Line = LineToClient(scope.Location.Location.Position.Range.Start.Line),
                        EndLine = LineToClient(scope.Location.Location.Position.Range.End.Line),
                        Column = ColumnToClient(scope.Location.Location.Position.Range.Start.Character),
                        EndColumn = ColumnToClient(scope.Location.Location.Position.Range.End.Character),
                        NamedVariables = internals.Count,
                        Name = "Internals",
                        Source = new Source()
                        {
                            Path = scope.Location.Location.File.ToString(),
                            Name = Path.GetFileName(scope.Location.Location.File.ToString()),
                        },
                        VariablesReference = ids.Next(),
                    }, internals));
                }
                */

                if (arguments.Count > 0)
                {
                    frameScopes.Add((new Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Scope()
                    {
                        Line = LineToClient(scope.Location.Location.Position.Range.Start.Line),
                        EndLine = LineToClient(scope.Location.Location.Position.Range.End.Line),
                        Column = ColumnToClient(scope.Location.Location.Position.Range.Start.Character),
                        EndColumn = ColumnToClient(scope.Location.Location.Position.Range.End.Character),
                        NamedVariables = arguments.Count,
                        Name = "Arguments",
                        PresentationHint = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Scope.PresentationHintValue.Arguments,
                        Source = new Source()
                        {
                            Path = scope.Location.Location.File.ToString(),
                            Name = Path.GetFileName(scope.Location.Location.File.ToString()),
                        },
                        VariablesReference = CurrentUniqueIds.Next(),
                    }, arguments));
                }

                if (locals.Count > 0)
                {
                    frameScopes.Add((new Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Scope()
                    {
                        Line = LineToClient(scope.Location.Location.Position.Range.Start.Line),
                        EndLine = LineToClient(scope.Location.Location.Position.Range.End.Line),
                        Column = ColumnToClient(scope.Location.Location.Position.Range.Start.Character),
                        EndColumn = ColumnToClient(scope.Location.Location.Position.Range.End.Character),
                        NamedVariables = locals.Count,
                        Name = "Locals",
                        PresentationHint = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Scope.PresentationHintValue.Locals,
                        Source = new Source()
                        {
                            Path = scope.Location.Location.File.ToString(),
                            Name = Path.GetFileName(scope.Location.Location.File.ToString()),
                        },
                        VariablesReference = CurrentUniqueIds.Next(),
                    }, locals));
                }

                if (returnValue.Count > 0)
                {
                    frameScopes.Add((new Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Scope()
                    {
                        Line = LineToClient(scope.Location.Location.Position.Range.Start.Line),
                        EndLine = LineToClient(scope.Location.Location.Position.Range.End.Line),
                        Column = ColumnToClient(scope.Location.Location.Position.Range.Start.Character),
                        EndColumn = ColumnToClient(scope.Location.Location.Position.Range.End.Character),
                        NamedVariables = returnValue.Count,
                        Name = "Return Value",
                        PresentationHint = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Scope.PresentationHintValue.ReturnValue,
                        Source = new Source()
                        {
                            Path = scope.Location.Location.File.ToString(),
                            Name = Path.GetFileName(scope.Location.Location.File.ToString()),
                        },
                        VariablesReference = CurrentUniqueIds.Next(),
                    }, returnValue));
                }
            }

            StackFrames.Add((stackFrame, frameScopes));
        }
    }

    static Uri ToUri(string path) =>
        path.Contains("//:")
        ? new Uri(path)
        : new Uri($"file://{path}");

    void TrySetBreakpoints()
    {
        void _(List<Breakpoint> pending)
        {
            if (Processor is null) return;

            for (int i = 0; i < pending.Count; i++)
            {
                Breakpoint breakpoint = pending[i];

                if (!breakpoint.Line.HasValue)
                {
                    pending.RemoveAt(i--);
                    Protocol.SendEvent(new BreakpointEvent()
                    {
                        Breakpoint = breakpoint,
                        Reason = BreakpointEvent.ReasonValue.Removed,
                    });
                    continue;
                }

                SinglePosition pos = new(LineFromClient(breakpoint.Line.Value), ColumnFromClient(breakpoint.Column ?? clientsFirstColumn));
                Range<int> selectedInstructions = default;

                foreach (SourceCodeLocation item in Processor.DebugInformation.SourceCodeLocations)
                {
                    if (item.Location.File != ToUri(breakpoint.Source.Path)) continue;
                    if (!item.Location.Position.Range.Contains(pos)) continue;
                    if (selectedInstructions == 0 || item.Instructions.Size() < selectedInstructions.Size())
                    {
                        selectedInstructions = item.Instructions;
                    }
                }

                if (selectedInstructions == 0)
                {
                    if (pending != InvalidBreakpoints)
                    {
                        breakpoint.Message = $"Invalid location";
                        breakpoint.Verified = false;
                        breakpoint.Reason = Breakpoint.ReasonValue.Failed;
                        Protocol.SendEvent(new BreakpointEvent()
                        {
                            Breakpoint = breakpoint,
                            Reason = BreakpointEvent.ReasonValue.Changed,
                        });
                        pending.RemoveAt(i--);
                        InvalidBreakpoints.Add(breakpoint);
                    }
                    continue;
                }

                breakpoint.Message = null;
                breakpoint.Verified = true;

                Breakpoints.Add((breakpoint, selectedInstructions.Start));
                pending.RemoveAt(i--);

                Protocol.SendEvent(new BreakpointEvent()
                {
                    Breakpoint = breakpoint,
                    Reason = BreakpointEvent.ReasonValue.Changed,
                });
            }
        }

        _(UnverifiedBreakpoints);
    }

    int clientsFirstLine;
    int clientsFirstColumn;

    int LineToClient(int line) => line + clientsFirstLine;
    int LineFromClient(int line) => line - clientsFirstLine;

    int ColumnToClient(int column) => column + clientsFirstColumn;
    int ColumnFromClient(int column) => column - clientsFirstColumn;

    public void Run()
    {
        Protocol.Run();
        while (Protocol.IsRunning && !IsDisconnected)
        {
            SysThread.Sleep(50);
        }
        Log.WriteLine("Stopping protocol");
        Protocol.Stop();
    }
}
