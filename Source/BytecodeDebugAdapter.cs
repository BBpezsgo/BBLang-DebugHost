using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Threading;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using SysThread = System.Threading.Thread;

namespace DebugServer;

partial class BytecodeDebugAdapter : DebugAdapterBase
{
    readonly Lock SyncLock = new();
    UniqueIds BreakpointIds;

    readonly Logger _log;
    readonly ExtLogger _extLog;

    Logger Log => Protocol.IsRunning ? _extLog : _log;

    CompilerResult Compiled;
    BBLangGeneratorResult Generated;
    BytecodeProcessor? Processor;

    readonly Dictionary<Uri, List<Breakpoint>> InvalidBreakpoints = [];
    readonly Dictionary<Uri, List<CompiledBreakpoint>> Breakpoints = [];
    readonly List<(Breakpoint Breakpoint, InstructionBreakpoint InstructionBreakpoint, int Address)> InstructionBreakpoints = [];

    class CompiledBreakpoint(Breakpoint breakpoint, int instruction, SourceBreakpoint sourceBreakpoint, string condition, string hitCondition, string? logMessage)
    {
        public Breakpoint Breakpoint { get; } = breakpoint;
        public int Instruction { get; } = instruction;
        public SourceBreakpoint SourceBreakpoint { get; } = sourceBreakpoint;
        public string Condition { get; } = condition;
        public string HitCondition { get; } = hitCondition;
        public string? LogMessage { get; } = logMessage;
        public int HitCount { get; set; }
    }

    readonly struct FetchedVariable
    {
        public readonly StackElementInformation Value;

        public FetchedVariable(StackElementInformation value)
        {
            Value = value;
        }
    }

    enum FetchedScopeKind
    {
        ReturnValue,
        Locals,
        Arguments,
        Internals,
        Globals,
    }

    readonly struct FetchedScope
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

    readonly struct FetchedFrame
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

    readonly List<FetchedFrame> StackFrames = [];
    readonly List<(GeneralType Type, int Id, int Address, string ParentName)> IndirectVariables = [];
    UniqueIds CurrentUniqueIds;

    public BytecodeDebugAdapter(Stream stdIn, Stream stdOut, Logger log)
    {
        AllowProceedEvent = new ManualResetEvent(true);
        DidProceedEvent = new ManualResetEvent(false);
        InitializeProtocolClient(stdIn, stdOut);
        _log = log;
        _extLog = new ExtLogger(Protocol);
    }

    void ResetSession()
    {
        IO = null;
        StackFrames.Clear();
        IndirectVariables.Clear();
        IsStopped = false;
        LastStopContext = null;
        ShouldStop = false;
        StopReason = null;
        CrashReason = null;
        Time = 0;
        AllowProceedEvent.Set();
        DidProceedEvent.Reset();
        StdOut.Clear();
        StdOutCommonTraceItem = null;
        StdOutModifiedAt = 0;

        RuntimeThread?.Join();

        RuntimeThread = null;
        IsRestarting = false;
        Processor = null;
    }

    void DisposeSession()
    {
        ResetSession();
        Compiled = default;
        Generated = default;
        InvalidBreakpoints.Clear();
        Breakpoints.Clear();
        InstructionBreakpoints.Clear();
        Processor = null;
        IsDisconnected = false;
        NoDebug = false;
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
            MemoryReference = address.Start.ToString(),
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
                case IReferenceType v:
                {
                    int pointerValue = memory.Get<int>(address.Start);
                    variable.Value = $"0x{Convert.ToString(pointerValue, 16)}";
                    if (StatementCompiler.FindSize(v.To, out _, out _, new RuntimeInfoProvider() { PointerSize = MainGeneratorSettings.Default.PointerSize }))
                    {
                        variable.VariablesReference = DiscoverIndirectVariables(pointerValue, v.To, memory, name, ref ids);
                    }
                    break;
                }
                case ArrayType v:
                {
                    if (v.Length.HasValue && StatementCompiler.FindSize(v.Of, out _, out _, new RuntimeInfoProvider() { PointerSize = MainGeneratorSettings.Default.PointerSize }))
                    {
                        variable.Value = "[...]";
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
                case FunctionType v:
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

        List<CallTraceItem> trace = [];
        DebugUtils.TraceStack(Processor.Memory, Processor.Registers.BasePointer, Processor.DebugInformation.StackOffsets, trace);
        trace.Insert(0, new CallTraceItem(Processor.Registers.BasePointer, Processor.Registers.CodePointer));

        Log.Trace($"Analzing {trace.Count} stack frames ...");

        foreach (CallTraceItem frame in trace)
        {
            if (frame.InstructionPointer < 0 || frame.InstructionPointer >= Processor.Code.Length)
            {
                Log.Trace($"Skipping invalid stack frame {frame} (IP out of range [0..{Processor.Code.Length}])");
                continue;
            }

            Log.Trace($"Analysing at IP {frame.InstructionPointer}");

            FunctionInformation f = Processor.DebugInformation.GetFunctionInformation(frame.InstructionPointer);

            List<FetchedScope> frameScopes = [];
            ImmutableArray<ScopeInformation> _scopes = Processor.DebugInformation.GetScopes(frame.InstructionPointer);
            FetchedVariable? globalVariablesAddress = null;

            if (frame.InstructionPointer == 0)
            {
                Log.Trace($"Skipping invalid stack frame {frame} (IP is 0)");
                continue;
            }

            if (!f.IsValid)
            {
                Log.Trace($"Skipping invalid stack frame {frame} (invalid function)");
                StackFrames.Add(new FetchedFrame(
                    CurrentUniqueIds.Next(),
                    frame,
                    f,
                    [],
                    [],
                    null
                ));
                continue;
            }

            if (_scopes.IsDefaultOrEmpty)
            {
                Log.Trace($"Skipping invalid stack frame {frame} (no scopes)");
                StackFrames.Add(new FetchedFrame(
                    CurrentUniqueIds.Next(),
                    frame,
                    f,
                    [],
                    [],
                    null
                ));
                continue;
            }

            Log.Trace($"Analyzing {_scopes.Length} scopes ...");

            foreach (ScopeInformation scope in _scopes)
            {
                List<FetchedVariable> globals = [];
                List<FetchedVariable> locals = [];
                List<FetchedVariable> arguments = [];
                List<FetchedVariable> internals = [];
                List<FetchedVariable> returnValue = [];

                foreach (StackElementInformation item in scope.Stack)
                {
                    Log.Trace($"{item.Type} {item.Identifier}");
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
                        Log.Trace($"Global variable offset captured");
                    }
                    //Log.Trace($"Ignoring internal stack item {item.Value.Type} {item.Value.Identifier} ({item.Value.Size} bytes at {(item.Value.BasePointerRelative ? "BP+" : "ABS+")}{item.Value.Address})");
                }

                if (arguments.Count > 0)
                {
                    frameScopes.Add(new FetchedScope(
                        CurrentUniqueIds.Next(),
                        FetchedScopeKind.Arguments,
                        [.. arguments],
                        scope
                    ));
                }

                if (locals.Count > 0)
                {
                    frameScopes.Add(new FetchedScope(
                        CurrentUniqueIds.Next(),
                        FetchedScopeKind.Locals,
                        [.. locals],
                        scope
                    ));
                }

                if (globals.Count > 0)
                {
                    frameScopes.Add(new FetchedScope(
                        CurrentUniqueIds.Next(),
                        FetchedScopeKind.Globals,
                        [.. globals],
                        scope
                    ));
                }

                if (returnValue.Count > 0)
                {
                    frameScopes.Add(new FetchedScope(
                        CurrentUniqueIds.Next(),
                        FetchedScopeKind.ReturnValue,
                        [.. returnValue],
                        scope
                    ));
                }
            }

            StackFrames.Add(new FetchedFrame(
                CurrentUniqueIds.Next(),
                frame,
                f,
                _scopes,
                [.. frameScopes],
                globalVariablesAddress
            ));
        }
    }

    static Uri ToUri(string path) =>
        path.Contains("//:")
        ? new Uri(path)
        : new Uri($"file://{path}");

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
        Log.Info("Stopping protocol");
        Protocol.Stop();
    }
}
