using System;
using System.Collections.Generic;
using System.Linq;

namespace Tix.IBMMQ.Bridge.Options;

public class MQBridgeOptions
{
    public Dictionary<string, ConnectionOptions> Connections { get; set; } = new();
    public List<QueuePairOptions> QueuePairs { get; set; } = new();

    public void Validate()
    {
        // Check if at least a pair is configured otherwise break
        if (QueuePairs.Count == 0)
            throw new InvalidOperationException("At least one queue pair must be configured.");

        // Check if all connection keys used in pairs are defined
        var missingConnKeys = QueuePairs
            .SelectMany(x => new[] { x.InboundConnection, x.OutboundConnection })
            .Distinct()
            .Except(Connections.Keys)
            .ToList();

        if (missingConnKeys.Count > 0)
            throw new InvalidOperationException($"Missing connection keys: {string.Join(", ", missingConnKeys)}");
    }
}
