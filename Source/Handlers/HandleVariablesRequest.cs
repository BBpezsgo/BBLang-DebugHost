using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override VariablesResponse HandleVariablesRequest(VariablesArguments arguments)
    {
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
                        Range<int> address = variable.Value.GetRange(Processor.Registers.BasePointer, Processor.StackStart);
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
                            GeneralType fieldType = GeneralType.InsertTypeParameters(item.Type, v.TypeArguments) ?? item.Type;
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
