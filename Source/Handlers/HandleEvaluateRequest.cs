using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override EvaluateResponse HandleEvaluateRequest(EvaluateArguments arguments)
    {
        if (Processor is null) return new EvaluateResponse();

        List<ExpressionVariable> variables = [];

        if (arguments.FrameId.HasValue)
        {
            if (StackFrames.Count > 0)
            {
                FetchedFrame item = StackFrames[0];
                if (item.Id != arguments.FrameId) return new EvaluateResponse();

                foreach (FetchedScope scope in item.Scopes)
                {
                    foreach (FetchedVariable variable in scope.Variables)
                    {
                        if (variable.Value.Kind == StackElementKind.Internal) continue;

                        int address = variable.Value.AbsoluteAddress(item.Raw.BasePointer, Processor.StackStart);
                        ExpressionVariable v = new(variable.Value.Identifier, address, variable.Value.Type);
                        variables.Add(v);
                        Log.WriteLine(v);
                    }
                }
            }
        }

        try
        {
            DiagnosticsCollection diagnostics = new();

            CompilerResult compiled = StatementCompiler.CompileExpression(arguments.Expression, new CompilerSettings(CodeGeneratorForMain.DefaultCompilerSettings)
            {
                ExternalFunctions = Compiled.ExternalFunctions,
                AdditionalImports = [],
                ExternalConstants = [],
                SourceProviders = [],
                IsExpression = true,
                ExpressionVariables = [.. variables],
            }, diagnostics, Compiled);

            if (diagnostics.HasErrors)
            {
                StringBuilder b = new();
                diagnostics.WriteErrorsTo(b);
                Protocol.SendEvent(new OutputEvent()
                {
                    Output = b.ToString(),
                    Severity = OutputEvent.SeverityValue.Error,
                });
                return new EvaluateResponse();
            }

            if (compiled.Statements.Length != 1)
            {
                Protocol.SendEvent(new OutputEvent()
                {
                    Output = $"Expression should only have one value, {compiled.Statements.Length} passed",
                    Severity = OutputEvent.SeverityValue.Error,
                });
                return new EvaluateResponse();
            }

            GeneralType expressionType = compiled.Statements[0] is CompiledExpression v && v.SaveValue ? v.Type : BuiltinType.Void;

            Log.WriteLine($"{compiled.Statements[0].GetType().Name} {compiled.Statements[0]}");

            BBLangGeneratorResult generated = CodeGeneratorForMain.Generate(compiled, new(MainGeneratorSettings.Default)
            {
                IsExpression = true,
            }, null, diagnostics);
            if (diagnostics.HasErrors)
            {
                StringBuilder b = new();
                diagnostics.WriteErrorsTo(b);
                Protocol.SendEvent(new OutputEvent()
                {
                    Output = b.ToString(),
                    Severity = OutputEvent.SeverityValue.Error,
                });
                return new EvaluateResponse();
            }

            byte[] memory = new byte[Processor.Memory.Length];
            Processor.Memory.CopyTo(memory, 0);

            BytecodeProcessor interpreter = new(
                BytecodeInterpreterSettings.Default,
                generated.Code,
                memory,
                generated.DebugInfo,
                compiled.ExternalFunctions,
                generated.GeneratedUnmanagedFunctions
            );

            interpreter.Registers.StackPointer = Processor.Registers.StackPointer;

            foreach (Instruction item in interpreter.Code)
            {
                Log.WriteLine(item.ToString());
            }

            interpreter.RunUntilCompletion();

            int resultAddress = interpreter.Registers.StackPointer;
            ReadOnlySpan<byte> m = memory;

            Log.WriteLine(resultAddress.ToString());

            switch (expressionType.FinalValue)
            {
                case BuiltinType w:
                    return w.Type switch
                    {
                        BasicType.Void => new EvaluateResponse()
                        {
                            Result = "void",
                            Type = expressionType.ToString(),
                        },
                        BasicType.Any => new EvaluateResponse()
                        {
                            Result = "?",
                            Type = expressionType.ToString(),
                        },
                        BasicType.U8 => new EvaluateResponse()
                        {
                            Result = m.Get<byte>(resultAddress).ToString(),
                            Type = expressionType.ToString(),
                        },
                        BasicType.I8 => new EvaluateResponse()
                        {
                            Result = m.Get<sbyte>(resultAddress).ToString(),
                            Type = expressionType.ToString(),
                        },
                        BasicType.U16 => new EvaluateResponse()
                        {
                            Result = m.Get<ushort>(resultAddress).ToString(),
                            Type = expressionType.ToString(),
                        },
                        BasicType.I16 => new EvaluateResponse()
                        {
                            Result = m.Get<short>(resultAddress).ToString(),
                            Type = expressionType.ToString(),
                        },
                        BasicType.U32 => new EvaluateResponse()
                        {
                            Result = m.Get<uint>(resultAddress).ToString(),
                            Type = expressionType.ToString(),
                        },
                        BasicType.I32 => new EvaluateResponse()
                        {
                            Result = m.Get<int>(resultAddress).ToString(),
                            Type = expressionType.ToString(),
                        },
                        BasicType.U64 => new EvaluateResponse()
                        {
                            Result = m.Get<ulong>(resultAddress).ToString(),
                            Type = expressionType.ToString(),
                        },
                        BasicType.I64 => new EvaluateResponse()
                        {
                            Result = m.Get<long>(resultAddress).ToString(),
                            Type = expressionType.ToString(),
                        },
                        BasicType.F32 => new EvaluateResponse()
                        {
                            Result = m.Get<float>(resultAddress).ToString(),
                            Type = expressionType.ToString(),
                        },
                        _ => throw new UnreachableException(),
                    };
                default:
                    return new EvaluateResponse()
                    {
                        Result = expressionType.ToString(),
                        Type = expressionType.ToString(),
                    };
            }
        }
        catch (Exception ex)
        {
            Protocol.SendEvent(new OutputEvent()
            {
                Output = ex.ToString(),
                Severity = OutputEvent.SeverityValue.Error,
            });
            return new EvaluateResponse();
        }
    }
}
