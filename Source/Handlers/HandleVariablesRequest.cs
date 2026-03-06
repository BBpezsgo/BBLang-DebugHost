using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override VariablesResponse HandleVariablesRequest(VariablesArguments arguments)
    {
        Log.Trace($"[Handler] Variables");

        if (Processor is null) return new VariablesResponse();

        using (SyncLock.EnterScope())
        {
            foreach (FetchedFrame frame in StackFrames)
            {
                foreach (FetchedScope scope in frame.Scopes)
                {
                    if (scope.Id != arguments.VariablesReference) continue;

                    List<Variable> result = [];
                    foreach (FetchedVariable variable in scope.Variables.Slice(arguments.Start, arguments.Count))
                    {
                        Range<int> address;
                        if (variable.Value.Kind == StackElementKind.GlobalVariable)
                        {
                            if (frame.GlobalVariablesAddress.HasValue)
                            {
                                int _v = frame.GlobalVariablesAddress.Value.Value.AbsoluteAddress(frame.Raw.BasePointer, Processor.StackStart);
                                int o = Processor.Memory.AsSpan().Get<int>(_v);
                                address = new Range<int>(variable.Value.Address + o, variable.Value.Address + variable.Value.Size + o);
                            }
                            else
                            {
                                Log.Warn($"Trying to handle global variable but no offset has been captured");
                                continue;
                            }
                        }
                        else if (variable.Value.Kind == StackElementKind.Internal && variable.Value.Identifier == "Exit Code")
                        {
                            address = new Range<int>(Processor.StackStart, Processor.StackStart + (ProcessorState.StackDirection * 4));
                        }
                        else
                        {
                            address = variable.Value.GetRange(frame.Raw.BasePointer, Processor.StackStart);
                        }

                        result.Add(ToVariable(address, variable.Value.Type, Processor.Memory, variable.Value.Identifier, ref CurrentUniqueIds));
                    }
                    return new VariablesResponse(result);
                }
            }

            foreach (var indirectVariable in IndirectVariables)
            {
                if (indirectVariable.Id != arguments.VariablesReference) continue;
                List<Variable> result = [];
                switch (indirectVariable.Type.FinalValue)
                {
                    case BuiltinType:
                    case PointerType:
                    case ReferenceType:
                    {
                        result.Add(ToVariable(indirectVariable.Address, indirectVariable.Type, Processor.Memory, $"*{indirectVariable.ParentName}", ref CurrentUniqueIds));
                        break;
                    }
                    case FunctionType v:
                    {
                        if (!v.HasClosure) throw new UnreachableException();
                        result.Add(ToVariable(indirectVariable.Address, indirectVariable.Type, Processor.Memory, $"*{indirectVariable.ParentName}", ref CurrentUniqueIds));
                        break;
                    }
                    case ArrayType v:
                    {
                        if (v.Length.HasValue && StatementCompiler.FindSize(v.Of, out int elementSize, out _, new RuntimeInfoProvider() { PointerSize = MainGeneratorSettings.Default.PointerSize }))
                        {
                            for (int i = 0; i < v.Length.Value; i++)
                            {
                                result.Add(ToVariable(indirectVariable.Address + (i * elementSize), v.Of, Processor.Memory, $"{indirectVariable.ParentName}[{i}]", ref CurrentUniqueIds));
                            }
                        }
                        break;
                    }
                    case StructType v:
                    {
                        int offset = 0;
                        foreach (CompiledField item in v.Struct.Fields)
                        {
                            GeneralType fieldType = GeneralType.TryInsertTypeParameters(item.Type, v.TypeArguments);
                            if (!StatementCompiler.FindSize(fieldType, out int fieldSize, out _, new RuntimeInfoProvider() { PointerSize = MainGeneratorSettings.Default.PointerSize }))
                            {
                                break;
                            }
                            result.Add(ToVariable(indirectVariable.Address + offset, fieldType, Processor.Memory, $"{indirectVariable.ParentName}.{item.Identifier.Content}", ref CurrentUniqueIds));
                            offset += fieldSize;
                        }
                        break;
                    }
                    case GenericType:
                    case AliasType:
                    default:
                        throw new UnreachableException();
                }
                return new VariablesResponse(result);
            }
        }

        return new VariablesResponse();
    }
}
