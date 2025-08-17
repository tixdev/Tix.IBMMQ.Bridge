#nullable enable
namespace Tix.IBMMQ.Bridge.Options;

public class ConnectionOptions
{
    public string QueueManagerName { get; set; } = string.Empty;
    public string ConnectionName { get; set; } = string.Empty; // host(port)
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    
    // TLS/SSL Configuration
    public bool UseTls { get; set; } = false;
    public string? SslCipherSpec { get; set; } = null; // e.g., "ECDHE_RSA_AES_256_GCM_SHA384", "ANY_TLS12_OR_HIGHER"
    public string? SslKeyRepository { get; set; } = null; // Path to keystore
    public string? SslKeyRepositoryPassword { get; set; } = null; // Keystore password
    public string? SslCertLabel { get; set; } = null; // Certificate label
    public bool SslFipsRequired { get; set; } = false; // FIPS mode
    public bool SslPeerNameRequired { get; set; } = false; // Verify peer name
}
