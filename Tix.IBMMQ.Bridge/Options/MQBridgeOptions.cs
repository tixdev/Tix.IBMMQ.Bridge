using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

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

        var noQueueTest = QueuePairs.Where(x =>
            string.IsNullOrWhiteSpace(x.InboundQueue) ||
            string.IsNullOrWhiteSpace(x.OutboundQueue)
        ).ToList();
        if (noQueueTest.Any())
            throw new InvalidOperationException($"Missing inbound or outbound queue in queue pair {noQueueTest[0].InboundQueue}");

        var noChannelTest = QueuePairs.Where(x => 
            string.IsNullOrWhiteSpace(x.InboundChannel) ||
            string.IsNullOrWhiteSpace(x.OutboundChannel)
        ).ToList();
        if (noChannelTest.Any())
            throw new InvalidOperationException($"Missing inbound or outbound channel in queue pair {noChannelTest[0].InboundQueue}");
    }

    /// <summary>
    /// Short json settings parser:
    /// 
    /// {
    ///    "MQBridge": {
    ///        "InboundConnection": {
    ///            ...
    ///            "Channel": "default channel if it (almost) never changes"
    ///        },
    ///        "OutboundConnection": {
    ///            ...
    ///            "Channel": "default channel if it (almost) never changes"
    ///        },
    ///        "QueuePairs": [
    ///            { "In": "queue name", "Out": "blank if = In", InChannel = "for channel exception", OutChannel = "for channel exception" },
    ///            { "In": "queue name 2" },
    ///            { "In": "queue name 3" },            
    ///            { "In": "queue name 4", "Out": "different queue name 4 " },
    ///        ]
    ///    }
    ///}
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public static MQBridgeOptions ParseJsonReduxVerion(string fileName)
    {
        const string inConn = "In";
        const string outConn = "Out";

        var root = new ConfigurationBuilder()
            .AddJsonFile(fileName)
            .Build()
            .GetSection("MQBridge");

        var conf = new MQBridgeOptions();
        var inConnSection = root.GetSection("InboundConnection");
        var outConnSection = root.GetSection("OutboundConnection");
        
        conf.Connections.Add(inConn, inConnSection.Get<ConnectionOptions>());
        conf.Connections.Add(outConn, outConnSection.Get<ConnectionOptions>());

        foreach (var pair in root.GetSection("QueuePairs").GetChildren())
        {
            var qp = new QueuePairOptions() 
            {
                InboundConnection = inConn,
                InboundChannel = pair.GetValue<string>("InChannel"),
                InboundQueue = pair.GetValue<string>("In"),
                OutboundConnection = outConn,
                OutboundChannel = pair.GetValue<string>("OutChannel"),
                OutboundQueue = pair.GetValue<string>("Out")
            };

            if (string.IsNullOrWhiteSpace(qp.InboundChannel))
                qp.InboundChannel = inConnSection["Channel"];
            
            if (string.IsNullOrWhiteSpace(qp.OutboundChannel))
                qp.OutboundChannel = outConnSection["Channel"];
            
            if (string.IsNullOrWhiteSpace(qp.OutboundQueue))
                qp.OutboundQueue = qp.InboundQueue; // Out name = In queue name

            conf.QueuePairs.Add(qp);
        }

        conf.Validate();

        return conf;
    }

    public string Serialize()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        
        return JsonSerializer.Serialize(
            new { MQBridge = this },
            options
            );
    }
}
