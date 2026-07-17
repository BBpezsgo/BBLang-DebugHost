using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

public abstract partial class BytecodeDebugAdapterBase : DebugAdapterBase
{
    protected readonly Lock SyncLock = new();
    protected UniqueIds BreakpointIds;

    protected readonly Logger _log;
    protected readonly ExtLogger _extLog;
    protected abstract bool IsStopped { get; }
    protected RuntimeException? CrashReason;
    protected bool NoDebug;

    protected Logger Log => Protocol.IsRunning ? _extLog : _log;

    protected abstract CompilerResult Compiled { get; }
    protected abstract ReadOnlyProcessorState Processor { get; }
    protected abstract CompiledDebugInformation DebugInformation { get; }

    protected readonly Dictionary<Uri, List<Breakpoint>> _invalidBreakpoints = new();
    protected readonly Dictionary<Uri, List<CompiledBreakpoint>> _breakpoints = new();
    protected readonly List<(Breakpoint Breakpoint, InstructionBreakpoint InstructionBreakpoint, int Address)> _instructionBreakpoints = new();

    protected class CompiledBreakpoint
    {
        public Breakpoint Breakpoint { get; }
        public int Instruction { get; }
        public SourceBreakpoint SourceBreakpoint { get; }
        public string Condition { get; }
        public string HitCondition { get; }
        public string? LogMessage { get; }
        public int HitCount { get; set; }

        public CompiledBreakpoint(Breakpoint breakpoint, int instruction, SourceBreakpoint sourceBreakpoint, string condition, string hitCondition, string? logMessage)
        {
            Breakpoint = breakpoint;
            Instruction = instruction;
            SourceBreakpoint = sourceBreakpoint;
            Condition = condition;
            HitCondition = hitCondition;
            LogMessage = logMessage;
        }
    }

    protected readonly struct FetchedVariable
    {
        public readonly StackElementInformation Value;

        public FetchedVariable(StackElementInformation value) => Value = value;
    }

    protected enum FetchedScopeKind
    {
        ReturnValue,
        Locals,
        Arguments,
        Internals,
        Globals,
    }

    protected readonly struct FetchedScope
    {
        public readonly int Id;
        public readonly FetchedScopeKind Kind;
        public readonly ImmutableArray<FetchedVariable> Variables;
        public readonly ScopeInformation Value;

        public FetchedScope(int id, FetchedScopeKind kind, ImmutableArray<FetchedVariable> variables, ScopeInformation value)
        {
            Id = id;
            Kind = kind;
            Variables = variables;
            Value = value;
        }
    }

    protected readonly struct FetchedFrame
    {
        public readonly int Id;
        public readonly CallTraceItem Raw;
        public readonly FunctionInformation Function;
        public readonly ImmutableArray<ScopeInformation> RawScopes;
        public readonly ImmutableArray<FetchedScope> Scopes;
        public readonly FetchedVariable? GlobalVariablesAddress;

        public FetchedFrame(int id, CallTraceItem raw, FunctionInformation function, ImmutableArray<ScopeInformation> rawScopes, ImmutableArray<FetchedScope> scopes, FetchedVariable? globalVariablesAddress)
        {
            Id = id;
            Raw = raw;
            Function = function;
            RawScopes = rawScopes;
            Scopes = scopes;
            GlobalVariablesAddress = globalVariablesAddress;
        }
    }

    protected readonly List<FetchedFrame> StackFrames = new();
    protected readonly List<(GeneralType Type, int Id, int Address, string ParentName)> IndirectVariables = new();
    protected UniqueIds CurrentUniqueIds;

    public BytecodeDebugAdapterBase(Stream stdIn, Stream stdOut, Logger log)
    {
        InitializeProtocolClient(stdIn, stdOut);
        _log = log;
        _extLog = new ExtLogger(Protocol);
    }

    protected virtual void ResetSession()
    {
        StackFrames.Clear();
        IndirectVariables.Clear();
        CrashReason = null;
    }

    protected virtual void DisposeSession()
    {
        ResetSession();

        _invalidBreakpoints.Clear();
        _breakpoints.Clear();
        _instructionBreakpoints.Clear();
        NoDebug = false;
    }

    protected Variable ToVariable(int address, GeneralType type, ReadOnlySpan<byte> memory, string name, ref UniqueIds ids)
    {
        if (!StatementCompiler.FindSize(type, out int size, out _, CodeGeneratorForMain.DefaultCompilerSettings.RuntimeInfo))
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

    protected static bool TryGetInteger(Range<int> address, ReadOnlySpan<byte> memory, GeneralType type, out int integer)
    {
        integer = default;
        if (type.FinalValue is not BuiltinType builtinType) return false;

        switch (builtinType.Type)
        {
            case BasicType.U8:
                integer = memory.Get<byte>(address.Start);
                return true;
            case BasicType.I8:
                integer = memory.Get<sbyte>(address.Start);
                return true;
            case BasicType.U16:
                integer = memory.Get<ushort>(address.Start);
                return true;
            case BasicType.I16:
                integer = memory.Get<short>(address.Start);
                return true;
            case BasicType.U32:
                integer = (int)memory.Get<uint>(address.Start);
                return true;
            case BasicType.I32:
                integer = memory.Get<int>(address.Start);
                return true;
            case BasicType.Void:
            case BasicType.Any:
            case BasicType.U64:
            case BasicType.I64:
            case BasicType.F32:
            default:
                return false;
        }
    }

    protected static string? GetInternalType(GeneralType type) => type is AliasType aliasType
        && aliasType.Definition.Definition.Attributes.TryGetAttribute(AttributeConstants.InternalType, out AttributeUsage? attribute)
        && attribute.TryGetValue(out string? internalType)
        ? internalType
        : null;

    protected Variable ToVariable(Range<int> address, GeneralType type, ReadOnlySpan<byte> memory, string name, ref UniqueIds ids)
    {
        Variable variable = new()
        {
            Type = type.ToString(),
            Name = name,
            Value = "?",
            MemoryReference = address.Start.ToString(),
        };

        if (address.Start < 0 || address.End >= memory.Length)
        {
            variable.Value = "<invalid>";
        }
        else
        {
            string? internalType = GetInternalType(type);

            switch (type.FinalValue)
            {
                case BuiltinType v:
                    if (internalType == InternalTypes.Boolean && TryGetInteger(address, memory, type, out int integer))
                    {
                        variable.Value = integer == 0 ? "false" : "true";
                        break;
                    }
                    else if (internalType == InternalTypes.Char && TryGetInteger(address, memory, type, out integer))
                    {
                        variable.Value = $"'{((char)integer).Escape()}'";
                        break;
                    }

                    variable.Value = v.Type switch
                    {
                        BasicType.Void => "void",
                        BasicType.Any => "?",
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
                case IReferenceType v:
                {
                    int pointerValue = memory.Get<int>(address.Start);
                    variable.Value = $"0x{Convert.ToString(pointerValue, 16)}";

                    if (v.To.Is(out ArrayType? toArray)
                        && (internalType == InternalTypes.String || GetInternalType(toArray.Of) == InternalTypes.Char)
                        && StatementCompiler.FindSize(toArray.Of, out int elementSize, out _, CodeGeneratorForMain.DefaultCompilerSettings.RuntimeInfo))
                    {
                        StringBuilder valueBuilder = new();
                        bool finished = false;
                        for (int i = 0; i < 16; i++)
                        {
                            if (TryGetInteger(new Range<int>(pointerValue + (i * elementSize), pointerValue + ((i + 1) * elementSize) - 1), memory, toArray.Of, out int element))
                            {
                                if (element == 0)
                                {
                                    finished = true;
                                    break;
                                }
                                else
                                {
                                    _ = valueBuilder.Append((char)element);
                                }
                            }
                            else
                            {
                                goto failed;
                            }
                        }
                        variable.Value = $"{variable.Value} \"{valueBuilder.ToString().Escape()}{(finished ? "\"" : "...")}";
                    }
                failed:

                    if (StatementCompiler.FindSize(v.To, out _, out _, CodeGeneratorForMain.DefaultCompilerSettings.RuntimeInfo))
                    {
                        variable.VariablesReference = DiscoverIndirectVariables(pointerValue, v.To, memory, name, ref ids);
                    }
                    break;
                }
                case ArrayType v:
                {
                    if (v.Length.HasValue && StatementCompiler.FindSize(v.Of, out int elementSize, out _, CodeGeneratorForMain.DefaultCompilerSettings.RuntimeInfo))
                    {
                        variable.Value = "[...]";

                        if (internalType == InternalTypes.String || GetInternalType(v.Of) == InternalTypes.Char)
                        {
                            StringBuilder valueBuilder = new();
                            for (int i = 0; i < v.Length.Value; i++)
                            {
                                if (TryGetInteger(new Range<int>(address.Start + (i * elementSize), address.Start + ((i + 1) * elementSize) - 1), memory, v.Of, out int element))
                                {
                                    _ = valueBuilder.Append((char)element);
                                }
                                else
                                {
                                    goto failed;
                                }
                            }
                            variable.Value = $"\"{valueBuilder.ToString().Escape()}\"";
                        }
                    failed:

                        variable.IndexedVariables = v.Length.Value;
                        variable.VariablesReference = DiscoverIndirectVariables(address.Start, v, memory, name, ref ids);
                    }
                    else
                    {
                        variable.Value = "[?]";
                    }
                    break;
                }
                case StructType v:
                {
                    variable.Value = "{...}";
                    variable.VariablesReference = DiscoverIndirectVariables(address.Start, v, memory, name, ref ids);
                    variable.NamedVariables = v.Struct.Fields.Length;
                    break;
                }
                case FunctionType:
                {
                    int pointerValue = memory.Get<int>(address.Start);
                    variable.Value = $"0x{Convert.ToString(pointerValue, 16)}";
                    break;
                }
                case GenericType:
                case AliasType:
                case EnumType:
                default:
                    throw new UnreachableException();
            }
        }

        return variable;
    }

    protected int DiscoverIndirectVariables(int pointerValue, GeneralType type, ReadOnlySpan<byte> memory, string parentName, ref UniqueIds ids)
    {
        if (pointerValue > 0 && pointerValue < memory.Length)
        {
            foreach ((GeneralType Type, int Id, int Address, string ParentName) indirectVariable in IndirectVariables)
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

    protected void GatherInformation()
    {
        bool logs = false;
        StackFrames.Clear();
        IndirectVariables.Clear();
        CurrentUniqueIds = new UniqueIds();

        if (!Processor.IsValid) return;

        List<CallTraceItem> trace = new();
        DebugUtils.TraceStack(Processor.Memory, Processor.Registers.BasePointer, DebugInformation.StackOffsets, trace);
        trace.Insert(0, new CallTraceItem(Processor.Registers.BasePointer, Processor.Registers.CodePointer));

        if (logs) Log.Trace($"Analyzing {trace.Count} stack frames ...");

        foreach (CallTraceItem frame in trace)
        {
            if (frame.InstructionPointer <= 0 || frame.InstructionPointer >= Processor.Code.Length)
            {
                if (logs) Log.Trace($"Skipping invalid stack frame {frame} (IP out of range [1..{Processor.Code.Length}])");
                continue;
            }

            if (logs) Log.Trace($"Analysing stack frame {frame}");

            FunctionInformation f = DebugInformation.GetFunctionInformation(frame.InstructionPointer);

            if (!f.IsValid)
            {
                if (logs) Log.Trace($"Skipping invalid stack frame {frame} (invalid function)");
                StackFrames.Add(new FetchedFrame(
                    CurrentUniqueIds.Next(),
                    frame,
                    f,
                    ImmutableArray<ScopeInformation>.Empty,
                    ImmutableArray<FetchedScope>.Empty,
                    null
                ));
                continue;
            }

            ImmutableArray<ScopeInformation> _scopes = DebugInformation.GetScopes(frame.InstructionPointer);

            if (_scopes.IsDefaultOrEmpty)
            {
                if (logs) Log.Trace($"Skipping invalid stack frame {frame} (no scopes)");
                StackFrames.Add(new FetchedFrame(
                    CurrentUniqueIds.Next(),
                    frame,
                    f,
                    ImmutableArray<ScopeInformation>.Empty,
                    ImmutableArray<FetchedScope>.Empty,
                    null
                ));
                continue;
            }

            if (logs) Log.Trace($"Analyzing {_scopes.Length} scopes ...");

            List<FetchedScope> frameScopes = new();
            FetchedVariable? globalVariablesAddress = null;

            foreach (ScopeInformation scope in _scopes)
            {
                List<FetchedVariable> globals = new();
                List<FetchedVariable> locals = new();
                List<FetchedVariable> arguments = new();
                List<FetchedVariable> internals = new();
                List<FetchedVariable> returnValue = new();

                foreach (StackElementInformation item in scope.Stack)
                {
                    //Log.Trace($"{item.Type} {item.Identifier}");
                    (item.Kind switch
                    {
                        StackElementKind.Internal => item.Identifier is "Return Value" or "Exit Code" ? returnValue : internals,
                        StackElementKind.Variable => locals,
                        StackElementKind.GlobalVariable => globals,
                        StackElementKind.Parameter => arguments,
                        _ => throw new UnreachableException(),
                    }).Add(new FetchedVariable(item));
                }

                foreach (FetchedVariable item in internals)
                {
                    if (item.Value.Identifier == "Absolute Global Offset")
                    {
                        globalVariablesAddress = item;
                        //Log.Trace($"Global variable offset captured");
                    }
                    //Log.Trace($"Ignoring internal stack item {item.Value.Type} {item.Value.Identifier} ({item.Value.Size} bytes at {(item.Value.BasePointerRelative ? "BP+" : "ABS+")}{item.Value.Address})");
                }

                if (arguments.Count > 0)
                {
                    frameScopes.Add(new FetchedScope(
                        CurrentUniqueIds.Next(),
                        FetchedScopeKind.Arguments,
                        arguments.ToImmutableArray(),
                        scope
                    ));
                }

                if (locals.Count > 0)
                {
                    frameScopes.Add(new FetchedScope(
                        CurrentUniqueIds.Next(),
                        FetchedScopeKind.Locals,
                        locals.ToImmutableArray(),
                        scope
                    ));
                }

                if (globals.Count > 0)
                {
                    frameScopes.Add(new FetchedScope(
                        CurrentUniqueIds.Next(),
                        FetchedScopeKind.Globals,
                        globals.ToImmutableArray(),
                        scope
                    ));
                }

                if (returnValue.Count > 0)
                {
                    frameScopes.Add(new FetchedScope(
                        CurrentUniqueIds.Next(),
                        FetchedScopeKind.ReturnValue,
                        returnValue.ToImmutableArray(),
                        scope
                    ));
                }
            }

            StackFrames.Add(new FetchedFrame(
                CurrentUniqueIds.Next(),
                frame,
                f,
                _scopes,
                frameScopes.ToImmutableArray(),
                globalVariablesAddress
            ));
        }
    }

    protected virtual Source ToSource(Uri uri) => new()
    {
        Name = Path.GetFileName(uri.ToString()),
        Path = uri.ToString(),
    };

    protected virtual Uri ToUri(Source source) =>
        source.Path.Contains("//:")
        ? new Uri(source.Path)
        : new Uri($"file://{source.Path}");

    protected int ClientsFirstLine { get; private set; }
    protected int ClientsFirstColumn { get; private set; }

    protected int LineToClient(int line) => line + ClientsFirstLine;
    protected int LineFromClient(int line) => line - ClientsFirstLine;

    protected int ColumnToClient(int column) => column + ClientsFirstColumn;
    protected int ColumnFromClient(int column) => column - ClientsFirstColumn;

    public abstract void Run();
}
