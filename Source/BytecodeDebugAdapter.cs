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

    readonly Dictionary<Uri, List<Breakpoint>> InvalidBreakpoints = [];
    readonly Dictionary<Uri, List<(Breakpoint Breakpoint, int Instruction, SourceBreakpoint SourceBreakpoint)>> Breakpoints = [];
    readonly List<(Breakpoint Breakpoint, InstructionBreakpoint InstructionBreakpoint, int Address)> InstructionBreakpoints = [];

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

        public FetchedFrame(int id, CallTraceItem raw, FunctionInformation function, ImmutableArray<ScopeInformation> rawScopes, ImmutableArray<FetchedScope> scopes)
        {
            Id = id;
            Raw = raw;
            Function = function;
            RawScopes = rawScopes;
            Scopes = scopes;
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
        Log = log;
    }

    void ResetSession()
    {
        Compiled = default;
        Generated = default;
        InvalidBreakpoints.Clear();
        Breakpoints.Clear();
        InstructionBreakpoints.Clear();
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
    }

    void DisposeSession()
    {
        ResetSession();
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
                case PointerType v:
                {
                    int pointerValue = memory.Get<int>(address.Start);
                    variable.Value = $"0x{Convert.ToString(pointerValue, 16)}";
                    variable.VariablesReference = DiscoverIndirectVariables(pointerValue, v.To, memory, name, ref ids);
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

        foreach (CallTraceItem frame in trace)
        {
            if (frame.InstructionPointer < 0 || frame.InstructionPointer >= Processor.Code.Length) continue;

            FunctionInformation f = Processor.DebugInformation.GetFunctionInformation(frame.InstructionPointer);
            int frameId = CurrentUniqueIds.Next();
            string? functionName = f.IsValid ? (f.Identifier ?? f.Function?.ToReadable(f.TypeArguments) ?? "<unknown function>") : null;

            List<FetchedScope> frameScopes = [];
            ImmutableArray<ScopeInformation> _scopes = Processor.DebugInformation.GetScopes(frame.InstructionPointer);

            foreach (ScopeInformation scope in _scopes)
            {
                List<FetchedVariable> locals = [];
                List<FetchedVariable> arguments = [];
                List<FetchedVariable> internals = [];
                List<FetchedVariable> returnValue = [];

                foreach (StackElementInformation item in scope.Stack)
                {
                    (item.Kind switch
                    {
                        StackElementKind.Internal => item.Identifier == "Return Value" ? returnValue : internals,
                        StackElementKind.Variable => locals,
                        StackElementKind.Parameter => arguments,
                        _ => throw new UnreachableException(),
                    }).Add(new FetchedVariable(item));
                }

                if (arguments.Count > 0)
                {
                    int id = CurrentUniqueIds.Next();
                    frameScopes.Add(new FetchedScope(
                        id,
                        FetchedScopeKind.Arguments,
                        [.. arguments],
                        scope
                    ));
                }

                if (locals.Count > 0)
                {
                    int id = CurrentUniqueIds.Next();
                    frameScopes.Add(new FetchedScope(
                        id,
                        FetchedScopeKind.Locals,
                        [.. locals],
                        scope
                    ));
                }

                if (returnValue.Count > 0)
                {
                    int id = CurrentUniqueIds.Next();
                    frameScopes.Add(new FetchedScope(
                        id,
                        FetchedScopeKind.ReturnValue,
                        [.. returnValue],
                        scope
                    ));
                }
            }

            StackFrames.Add(new FetchedFrame(
                frameId,
                frame,
                f,
                _scopes,
                [.. frameScopes]
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
        Log.WriteLine("Stopping protocol");
        Protocol.Stop();
    }
}
