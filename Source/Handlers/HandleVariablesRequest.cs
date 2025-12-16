using System.Collections.Generic;
using System.Diagnostics;
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
            foreach (var frame in StackFrames)
            {
                foreach (var scope in frame.Scopes)
                {
                    if (scope.Scope.VariablesReference != arguments.VariablesReference) continue;
                    List<Variable> variables = [];
                    int start = arguments.Start ?? 0;
                    int count = arguments.Count ?? (scope.Variables.Count - start);
                    for (int i = 0; i < count; i++)
                    {
                        int j = i + start;
                        if (j < 0) break;
                        if (j >= scope.Variables.Count) continue;
                        variables.Add(scope.Variables[j].Variable);
                    }
                    return new VariablesResponse(variables);
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
                    case FunctionType:
                    {
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
