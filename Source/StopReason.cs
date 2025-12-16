using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

abstract class StopReason
{

}

class StopReason_Crash : StopReason
{
    public required RuntimeException Exception { get; init; }
}

class StopReason_Breakpoint : StopReason
{
    public required Breakpoint Breakpoint { get; init; }
}

class StopReason_StepForward : StopReason
{
    public static readonly StopReason_StepForward Instance = new();
}

class StopReason_StepIn : StopReason
{
    public static readonly StopReason_StepIn Instance = new();
}

class StopReason_StepOut : StopReason
{
    public static readonly StopReason_StepOut Instance = new();
}

class StopReason_Pause : StopReason
{
    public static readonly StopReason_Pause Instance = new();
}
