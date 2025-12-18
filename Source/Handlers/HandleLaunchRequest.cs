using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using LanguageCore.Runtime;
using LanguageCore.Workspaces;
using LanguageServer;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Utilities;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override LaunchResponse HandleLaunchRequest(LaunchArguments arguments)
    {
        Log.WriteLine("HandleLaunchRequest");

        string fileName = arguments.ConfigurationProperties.GetValueAsString("program");
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ProtocolException("Launch failed because launch configuration did not specify 'program'.");
        }

        fileName = Path.GetFullPath(fileName);
        if (!File.Exists(fileName))
        {
            throw new ProtocolException("Launch failed because 'program' files does not exist.");
        }

        Reset();

        VirtualIO io = new();
        List<IExternalFunction> externalFunctions = BytecodeProcessor.GetExternalFunctions(io);
        io.OnStdOut += WriteStdout;

        DiagnosticsCollection diagnostics = new();

        Configuration config = Configuration.Parse(ConfigurationManager.Search(ToUri(fileName)), diagnostics);
        if (diagnostics.HasErrors)
        {
            StringBuilder b = new();
            diagnostics.WriteErrorsTo(b);
            Protocol.SendEvent(new OutputEvent()
            {
                Output = b.ToString(),
                Severity = OutputEvent.SeverityValue.Error,
            });
        }
        diagnostics.Clear();

        Compiled = StatementCompiler.CompileFile(fileName, new(CodeGeneratorForMain.DefaultCompilerSettings)
        {
            ExternalFunctions = [.. externalFunctions],
            AdditionalImports = [.. config.AdditionalImports],
            ExternalConstants = [.. config.ExternalConstants],
            SourceProviders = [
                new FileSourceProvider()
                {
                    ExtraDirectories = config.ExtraDirectories,
                },
            ],
            Optimizations = OptimizationSettings.None,
        }, diagnostics);
        if (diagnostics.HasErrors)
        {
            StringBuilder b = new();
            diagnostics.WriteErrorsTo(b);
            Protocol.SendEvent(new OutputEvent()
            {
                Output = b.ToString(),
                Severity = OutputEvent.SeverityValue.Error,
            });
            Protocol.SendEvent(new ExitedEvent() { ExitCode = -1 });
            Protocol.SendEvent(new TerminatedEvent());
            return new LaunchResponse();
        }

        Generated = CodeGeneratorForMain.Generate(Compiled, new MainGeneratorSettings(MainGeneratorSettings.Default)
        {
            Optimizations = GeneratorOptimizationSettings.None,
        }, null, diagnostics);
        if (diagnostics.HasErrors)
        {
            Protocol.SendEvent(new ExitedEvent() { ExitCode = -1 });
            Protocol.SendEvent(new TerminatedEvent());
            return new LaunchResponse();
        }

        Processor = new BytecodeProcessor(
            BytecodeInterpreterSettings.Default,
            Generated.Code,
            null,
            Generated.DebugInfo,
            Compiled.ExternalFunctions,
            Generated.GeneratedUnmanagedFunctions
        );

        if (arguments.ConfigurationProperties.GetValueAsBool("stopOnEntry") ?? false)
        {
            RequestStop(StopReason_Pause.Instance);
        }
        else
        {
            StopReason = null;
        }

        RuntimeThread = new(DebugThreadProc)
        {
            Name = "Runtime Thread"
        };

        return new LaunchResponse();
    }
}
