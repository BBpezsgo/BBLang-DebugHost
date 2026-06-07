using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

public abstract class StopReason
{

}

public class StopReason_Crash : StopReason
{
    public required RuntimeException Exception { get; init; }
}

public class StopReason_Breakpoint : StopReason
{
    public required Breakpoint Breakpoint { get; init; }
}

public class StopReason_StepForward : StopReason
{
    public static readonly StopReason_StepForward Instance = new();
}

public class StopReason_StepIn : StopReason
{
    public static readonly StopReason_StepIn Instance = new();
}

public class StopReason_StepOut : StopReason
{
    public static readonly StopReason_StepOut Instance = new();
}

public class StopReason_StepInstruction : StopReason
{
    public static readonly StopReason_StepInstruction Instance = new();
}

public class StopReason_Pause : StopReason
{
    public static readonly StopReason_Pause Instance = new();
}
