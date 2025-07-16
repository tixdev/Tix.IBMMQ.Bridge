namespace Tix.IBMMQ.Bridge.Options;

public class QueuePairOptions
{
    public string InboundConnection { get; set; } = string.Empty;
    public string InboundChannel { get; set; } = string.Empty;
    public string InboundQueue { get; set; } = string.Empty;
    public string OutboundConnection { get; set; } = string.Empty;
    public string OutboundChannel { get; set; } = string.Empty;
    public string OutboundQueue { get; set; } = string.Empty;
    public int PollIntervalSeconds { get; set; }
}
