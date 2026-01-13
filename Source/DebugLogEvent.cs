using System.Runtime.Serialization;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Serialization;
using Newtonsoft.Json;

namespace DebugServer;

public class DebugLogEvent : DebugEvent
{
    [JsonProperty("message", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Message { get; set; } = string.Empty;

    [JsonProperty("level", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string? Level { get; set; } = null;

    public DebugLogEvent() : base("adapterLog") { }
}
