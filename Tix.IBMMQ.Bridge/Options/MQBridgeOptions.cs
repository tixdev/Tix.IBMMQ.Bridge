using System.Collections.Generic;

namespace Tix.IBMMQ.Bridge.Options;

public class MQBridgeOptions
{
    public Dictionary<string, ConnectionOptions> Connections { get; set; } = new();
    public List<QueuePairOptions> QueuePairs { get; set; } = new();
}
