namespace Tix.IBMMQ.Bridge.Options;

public class ConnectionOptions
{
    public string QueueManagerName { get; set; } = string.Empty;
    public string ConnectionName { get; set; } = string.Empty; // host(port)
    public string Channel { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
