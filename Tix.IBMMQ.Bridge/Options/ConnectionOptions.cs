namespace Tix.IBMMQ.Bridge.Options;

public class ConnectionOptions
{
    public string QueueManagerName { get; set; } = string.Empty;
    public string ConnectionName { get; set; } = string.Empty; // host(port)
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public string SslCipherSpec { get; set; } = "TLS_AES_256_GCM_SHA384";
}
