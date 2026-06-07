using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapterBase
{
    protected override CompletionsResponse HandleCompletionsRequest(CompletionsArguments arguments)
    {
        SinglePosition p = new(LineFromClient(arguments.Line ?? ClientsFirstLine), ColumnFromClient(arguments.Column));
        Log.Trace($"[Handler] Completions ({p}, \"{arguments.Text}\")");

        List<CompletionItem> result = new();

        if (!Processor.IsValid) return new CompletionsResponse(result);
        if (!IsStopped) return new CompletionsResponse(result);

        List<ExpressionVariable> variables = arguments.FrameId.HasValue ? GetExpressionVariables(arguments.FrameId.Value) : new();

        try
        {
            DiagnosticsCollection diagnostics = new();

            CompilerResult compiled = StatementCompiler.CompileExpression(arguments.Text, new CompilerSettings(CodeGeneratorForMain.DefaultCompilerSettings)
            {
                ExternalFunctions = Compiled.ExternalFunctions,
                AdditionalImports = ImmutableArray<string>.Empty,
                ExternalConstants = ImmutableArray<LanguageCore.Runtime.ExternalConstant>.Empty,
                SourceProviders = ImmutableArray<ISourceProvider>.Empty,
                IsExpression = true,
                ExpressionVariables = variables.ToImmutableArray(),
            }, diagnostics, Compiled);

            ParserResult ast = compiled.RawTokens.FirstOrDefault(v => v.File == compiled.File).AST;
            if (!ast.IsNotEmpty) return new CompletionsResponse();

            List<Statement> contextStatement = new();
            Dictionary<string, int> functionOverloads = new();

            void AddExpressionItems()
            {
                foreach (CompiledFunctionDefinition function in compiled.FunctionDefinitions)
                {
                    if (!function.Definition.CanUse(compiled.File)) continue;

                    if (!functionOverloads.TryGetValue(function.Identifier, out int value)) value = 0;
                    functionOverloads[function.Identifier] = value + 1;
                }

                foreach (VariableDefinition variable in ast.TopLevelStatements.OfType<VariableDefinition>())
                {
                    if (!variable.CanUse(compiled.File)) continue;

                    result.Add(new CompletionItem()
                    {
                        Type = CompletionItemType.Variable,
                        Label = variable.Identifier.Content,
                    });
                }
            }

            foreach (Statement _statement in ast.EnumerateStatements())
            {
                if (_statement.Position.Range.Start > p) continue;
                if (contextStatement.Count == 0 || _statement.Position.AbsoluteRange.Start >= contextStatement[0].Position.AbsoluteRange.Start)
                {
                    for (int i = 0; i < contextStatement.Count; i++)
                    {
                        if (StatementWalker.Visit(contextStatement[i]).Contains(_statement)) continue;
                        Log.Trace($"{contextStatement[i].GetType().Name} {contextStatement[i]}");
                        contextStatement.RemoveAt(i--);
                    }
                    contextStatement.Add(_statement);
                }
            }

            foreach (Statement item in contextStatement)
            {
                Log.Trace($"{item.GetType().Name} {item}");
            }

            if (contextStatement.Count > 0)
            {
                if (contextStatement[^1] is IdentifierExpression identifier
                    && contextStatement.Count > 1
                    && contextStatement[^2] is FieldExpression fieldExpression)
                {
                    if (fieldExpression.Object == identifier)
                    {
                        if (fieldExpression.Object.CompiledType is not null)
                        {
                            List<GeneralType> checkTypes = new();

                            {
                                GeneralType prevType = fieldExpression.Object.CompiledType;
                                checkTypes.Add(prevType);
                                checkTypes.Add(new ReferenceType(prevType));
                                checkTypes.Add(new PointerType(prevType));
                                while (true)
                                {
                                    if (prevType.Is(out PointerType? pointerType2))
                                    {
                                        prevType = pointerType2.To;
                                        checkTypes.Add(prevType);
                                    }
                                    else if (prevType.Is(out ReferenceType? referenceType2))
                                    {
                                        prevType = referenceType2.To;
                                        checkTypes.Add(prevType);
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                            }

                            foreach (GeneralType prevType in checkTypes)
                            {
                                Log.Trace($"{prevType}");
                                if (prevType is StructType structType)
                                {
                                    foreach (CompiledField item in structType.Struct.Fields)
                                    {
                                        result.Add(new CompletionItem()
                                        {
                                            Type = CompletionItemType.Field,
                                            Label = item.Identifier,
                                            Detail = item.Type.ToString(),
                                        });
                                    }
                                }

                                foreach (CompiledFunctionDefinition function in compiled.FunctionDefinitions)
                                {
                                    if (!function.Definition.CanUse(compiled.File)) continue;
                                    if (function.Parameters.Length <= 0) continue;
                                    if (!function.Parameters[0].Definition.IsThis) continue;
                                    if (!function.Parameters[0].Type.Equals(prevType)) continue;

                                    if (!functionOverloads.TryGetValue(function.Identifier, out int value)) value = 0;
                                    functionOverloads[function.Identifier] = value + 1;
                                }
                            }
                        }
                        else
                        {
                            Log.Warn($"Missing type on {fieldExpression.Object.GetType().Name} {fieldExpression.Object}");
                        }
                    }
                    else
                    {
                        Log.Warn($"Field identifier mismatch: {fieldExpression.Object.GetType().Name} {fieldExpression.Object} != {identifier.GetType().Name} {identifier}");
                    }
                }
                else if (contextStatement[^1] is MissingExpression)
                {
                    AddExpressionItems();
                }
            }
            else
            {
                AddExpressionItems();
            }

            foreach ((string function, int overloads) in functionOverloads)
            {
                result.Add(new CompletionItem()
                {
                    Type = CompletionItemType.Function,
                    Label = function,
                    Detail = overloads <= 1 ? null : $"{overloads} overloads",
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        return new CompletionsResponse(result);
    }
}
