using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override CompletionsResponse HandleCompletionsRequest(CompletionsArguments arguments)
    {
        SinglePosition p = new(LineFromClient(arguments.Line ?? clientsFirstLine), ColumnFromClient(arguments.Column));
        Log.WriteLine($"HandleCompletionsRequest({p}, `{arguments.Text}`)");

        List<CompletionItem> result = [];

        if (Processor is null) return new CompletionsResponse(result);

        List<ExpressionVariable> variables = arguments.FrameId.HasValue ? GetExpressionVariables(arguments.FrameId.Value) : [];

        if (string.IsNullOrWhiteSpace(arguments.Text))
        {
            foreach (ExpressionVariable item in variables)
            {
                result.Add(new CompletionItem()
                {
                    Type = CompletionItemType.Variable,
                    Label = item.Name,
                    Detail = item.Type.ToString(),
                });
            }

            return new CompletionsResponse(result);
        }

        try
        {
            DiagnosticsCollection diagnostics = new();

            CompilerResult compiled = StatementCompiler.CompileExpression(arguments.Text, new CompilerSettings(CodeGeneratorForMain.DefaultCompilerSettings)
            {
                ExternalFunctions = Compiled.ExternalFunctions,
                AdditionalImports = [],
                ExternalConstants = [],
                SourceProviders = [],
                IsExpression = true,
                ExpressionVariables = [.. variables],
            }, diagnostics, Compiled);

            ParserResult ast = compiled.RawTokens.FirstOrDefault(v => v.File == compiled.File).AST;
            if (!ast.IsNotEmpty) return new CompletionsResponse();

            List<Statement> contextStatement = [];

            foreach (Statement _statement in ast.EnumerateStatements())
            {
                if (_statement.Position.Range.Start > p) continue;
                if (contextStatement.Count == 0 || _statement.Position.AbsoluteRange.Start >= contextStatement[0].Position.AbsoluteRange.Start)
                {
                    for (int i = 0; i < contextStatement.Count; i++)
                    {
                        if (StatementWalker.Visit(contextStatement[i]).Contains(_statement)) continue;
                        Log.Debug($"{contextStatement[i].GetType().Name} {contextStatement[i]}");
                        contextStatement.RemoveAt(i--);
                    }
                    contextStatement.Add(_statement);
                }
            }

            foreach (var item in contextStatement)
            {
                Log.Debug($"{item.GetType().Name} {item}");
            }

            if (contextStatement.Count > 0)
            {
                if (contextStatement[^1] is IdentifierExpression identifier
                    && contextStatement.Count > 1
                    && contextStatement[^2] is FieldExpression fieldExpression)
                {
                    if (fieldExpression.Identifier == identifier.Identifier)
                    {
                        if (fieldExpression.Object.CompiledType is not null)
                        {
                            List<GeneralType> checkTypes = [];

                            {
                                GeneralType prevType = fieldExpression.Object.CompiledType;
                                checkTypes.Add(prevType);
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

                            Dictionary<string, int> functionOverloads = [];

                            foreach (GeneralType prevType in checkTypes)
                            {
                                Log.Debug($"{prevType}");
                                if (prevType is StructType structType)
                                {
                                    foreach (CompiledField item in structType.Struct.Fields)
                                    {
                                        result.Add(new CompletionItem()
                                        {
                                            Type = CompletionItemType.Field,
                                            Label = item.Identifier.Content,
                                            Detail = item.Type.ToString(),
                                        });
                                    }
                                }

                                foreach (CompiledFunctionDefinition function in compiled.FunctionDefinitions)
                                {
                                    if (!function.CanUse(compiled.File)) continue;
                                    if (function.Parameters.Length <= 0) continue;
                                    if (!function.Parameters[0].IsThis) continue;
                                    if (!function.Parameters[0].Type.SameAs(prevType)) continue;

                                    if (!functionOverloads.TryGetValue(function.Identifier.Content, out int value)) value = 0;
                                    functionOverloads[function.Identifier.Content] = value + 1;
                                }
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

                            return new CompletionsResponse(result);
                        }
                        else
                        {
                            Log.Warn($"Missing type on {fieldExpression.Object.GetType().Name} {fieldExpression.Object}");
                        }
                    }
                    else
                    {
                        Log.Warn($"Field identifier {identifier.GetType().Name} {identifier} != {fieldExpression.Identifier.GetType().Name} {fieldExpression.Identifier}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return new CompletionsResponse(result);
        }

        return new CompletionsResponse(result);
    }
}
