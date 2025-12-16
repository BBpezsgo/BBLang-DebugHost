using System.Collections.Generic;
using System.IO;
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

        Breakpoints.Clear();

        VirtualIO io = new();
        StdOut.Clear();
        StdOutModifiedAt = 0;
        Time = 0;
        List<IExternalFunction> externalFunctions = BytecodeProcessor.GetExternalFunctions(io);
        io.OnStdOut += WriteStdout;

        DiagnosticsCollection diagnostics = new();

        Configuration config = Configuration.Parse(ConfigurationManager.Search(ToUri(fileName)), diagnostics);
        if (diagnostics.HasErrors)
        {
            StringBuilder b = new();
            diagnostics.WriteErrorsTo(b);
            SendOutput(b.ToString());
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
        }, diagnostics);
        if (diagnostics.HasErrors)
        {
            StringBuilder b = new();
            diagnostics.WriteErrorsTo(b);
            SendOutput(b.ToString());
            Protocol.SendEvent(new ExitedEvent() { ExitCode = -1 });
            Protocol.SendEvent(new TerminatedEvent());
            return new LaunchResponse();
        }

        Generated = CodeGeneratorForMain.Generate(Compiled, MainGeneratorSettings.Default, null, diagnostics);
        if (diagnostics.HasErrors)
        {
            Protocol.SendEvent(new ExitedEvent() { ExitCode = -1 });
            Protocol.SendEvent(new TerminatedEvent());
            return new LaunchResponse();
        }

        Processor = new BytecodeProcessor(BytecodeInterpreterSettings.Default, Generated.Code, null, Generated.DebugInfo, Compiled.ExternalFunctions, Generated.GeneratedUnmanagedFunctions);

        RequestStop(StopReason_Pause.Instance);
        //if (arguments.ConfigurationProperties.GetValueAsBool("stopAtEntry") ?? false)
        //{
        //    RequestStop(PauseStopReason.Instance);
        //}
        //else
        //{
        //    stopReason = null;
        //}

        RuntimeThread = new(DebugThreadProc)
        {
            Name = "Debug Loop Thread"
        };
        RuntimeThread.Start();

        return new LaunchResponse();
    }
}
