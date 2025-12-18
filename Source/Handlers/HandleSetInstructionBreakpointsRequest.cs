using System.Collections.Generic;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DebugServer;

partial class BytecodeDebugAdapter
{
    protected override SetInstructionBreakpointsResponse HandleSetInstructionBreakpointsRequest(SetInstructionBreakpointsArguments arguments)
    {
        if (Processor is null) return new SetInstructionBreakpointsResponse();

        InstructionBreakpoints.Clear();
        List<Breakpoint> result = [];

        foreach (InstructionBreakpoint requestedBreakpoint in arguments.Breakpoints)
        {
            if (!int.TryParse(requestedBreakpoint.InstructionReference, out int address))
            {
                result.Add(new Breakpoint()
                {
                    Id = BreakpointIds.Next(),
                    Verified = false,
                    Reason = Breakpoint.ReasonValue.Failed,
                    Message = "Invalid instruction reference",
                    InstructionReference = requestedBreakpoint.InstructionReference,
                });
                continue;
            }

            if (address < 0 || address >= Processor.Code.Length)
            {
                result.Add(new Breakpoint()
                {
                    Id = BreakpointIds.Next(),
                    Verified = false,
                    Reason = Breakpoint.ReasonValue.Failed,
                    Message = "Instruction address is out of range",
                    InstructionReference = requestedBreakpoint.InstructionReference,
                });
                continue;
            }
        
            Breakpoint breakpoint = new()
            {
                Id = BreakpointIds.Next(),
                Verified = true,
                InstructionReference = requestedBreakpoint.InstructionReference,
            };
            InstructionBreakpoints.Add((breakpoint, requestedBreakpoint, address));
            result.Add(breakpoint);
        }

        return new SetInstructionBreakpointsResponse(result);
    }
}
