using System.Collections.Generic;
using System.IO;
using System.Text;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using LanguageCore.Runtime;
using LanguageCore.Workspaces;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Utilities;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    VirtualIO? IO;

    protected override LaunchResponse HandleLaunchRequest(LaunchArguments arguments)
    {
        Log.Trace($"[Handler] Launch");

        string fileName = arguments.ConfigurationProperties.GetValueAsString("program");
        if (string.IsNullOrEmpty(fileName))
        {
            Log.Error($"Program is null or empty");
            throw new ProtocolException("Launch failed because launch configuration did not specify 'program'.");
        }

        fileName = Path.GetFullPath(fileName);
        if (!File.Exists(fileName))
        {
            Log.Error($"file \"{fileName}\" doesn't exists");
            throw new ProtocolException("Launch failed because 'program' files does not exist.");
        }

        Log.Trace($"Disposing previous session");
        DisposeSession();

        NoDebug = arguments.NoDebug ?? false;
        StopOnEntry = arguments.ConfigurationProperties.GetValueAsBool("stopOnEntry") ?? false;

        Log.Trace($"Preparing");
        IO = new();
        List<IExternalFunction> externalFunctions = BytecodeProcessor.GetExternalFunctions(IO);
        IO.OnData += WriteStdout;

        DiagnosticsCollection diagnostics = new();

        Log.Trace($"Parsing configuration");
        Configuration config = Configuration.Parse(ConfigurationManager.Search(ToUri(fileName)), diagnostics);
        if (diagnostics.HasErrors)
        {
            Log.Trace($"Diagnostic errors");
            StringBuilder b = new();
            diagnostics.WriteErrorsTo(b);
            Protocol.SendEvent(new OutputEvent()
            {
                Output = b.ToString(),
                Severity = OutputEvent.SeverityValue.Error,
            });
        }
        diagnostics.Clear();

        Log.Trace($"Compiling code");
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
            Log.Trace($"Diagnostic errors");
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

        Log.Trace($"Generating code");
        Generated = CodeGeneratorForMain.Generate(Compiled, new MainGeneratorSettings(MainGeneratorSettings.Default)
        {
            Optimizations = GeneratorOptimizationSettings.None,
        }, null, diagnostics);
        if (diagnostics.HasErrors)
        {
            Log.Trace($"Diagnostic errors");
            Protocol.SendEvent(new ExitedEvent() { ExitCode = -1 });
            Protocol.SendEvent(new TerminatedEvent());
            return new LaunchResponse();
        }

        Log.Trace($"Preparing processor");
        Processor = new BytecodeProcessor(
            BytecodeInterpreterSettings.Default,
            Generated.Code,
            null,
            Generated.DebugInfo,
            Compiled.ExternalFunctions,
            Generated.GeneratedUnmanagedFunctions
        );

        if (!NoDebug && StopOnEntry)
        {
            Log.Trace($"Stopping on entry");
            RequestStop(StopReason_Pause.Instance);
        }
        else
        {
            StopReason = null;
        }

        Log.Trace($"Creating thread");
        RuntimeThread = new(RuntimeImpl)
        {
            Name = "Runtime Thread"
        };

        Log.Trace($"Thread started");

        return new LaunchResponse();
    }
}
