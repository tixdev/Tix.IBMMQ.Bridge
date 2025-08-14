using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Xunit;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public sealed class MqTlsFixture : IAsyncLifetime
{
    public IContainer Container { get; private set; } = default!;
    public INetwork Network { get; private set; } = default!;
    public string MqAlias => "mq";
    public int MqInternalPort => 1414;
    public int Port { get; private set; }
    public int WebPort { get; private set; }

    public string Host => "localhost";

    public string TrustDir => _trustDir;
    
    private string _workDir = default!;
    private string _keysDir = default!;
    private string _trustDir = default!;
    private X509Certificate2 _caCert = default!;
    private X509Certificate2 _serverCert = default!;

    public async Task InitializeAsync()
    {
        Network = new NetworkBuilder().Build();
        await Network.CreateAsync();
        
        _workDir = Directory.CreateTempSubdirectory("mqtls").FullName;
        _keysDir = Path.Combine(_workDir, "keys", "qm1");
        _trustDir = Path.Combine(_workDir, "trust", "clients");
        Directory.CreateDirectory(_keysDir);
        Directory.CreateDirectory(_trustDir);

        _caCert = CreateCertificateAuthority("TestMQ-CA");
        _serverCert = CreateServerCertificate("localhost", _caCert);

        WriteMqPemFiles(_keysDir, _trustDir, _caCert, _serverCert);

        var mqscPath = Path.Combine(_workDir, "99-config.mqsc");
        File.WriteAllText(mqscPath, string.Join(Environment.NewLine, new[]
        {
            "ALTER QMGR CHLAUTH(ENABLED)",
            "DEFINE QLOCAL('DEV.QUEUE.1') REPLACE",
            "DEFINE CHANNEL('APP.TLS.SVRCONN') CHLTYPE(SVRCONN) TRPTYPE(TCP) " +
            "SSLCIPH(ANY_TLS12_OR_HIGHER) SSLCAUTH(OPTIONAL) REPLACE",
            // regola CHLAUTH permissiva solo per test
            "SET CHLAUTH('APP.TLS.SVRCONN') TYPE(ADDRESSMAP) ADDRESS('*') USERSRC(CHANNEL) ACTION(ADD)",
            "REFRESH SECURITY TYPE(SSL)"
        }));

        Container = new ContainerBuilder()
            .WithImage("ibm-mqadvanced-server-dev:9.4.3.0-arm64")
            .WithEnvironment("LICENSE", "accept")
            .WithEnvironment("MQ_QMGR_NAME", "QM1")
            .WithEnvironment("MQ_APP_PASSWORD", "passw0rd")
            .WithEnvironment("MQ_ADMIN_PASSWORD", "passw0rd")
            .WithBindMount(_keysDir, "/etc/mqm/pki/keys/qm1", AccessMode.ReadOnly)
            .WithBindMount(_trustDir, "/etc/mqm/pki/trust/clients", AccessMode.ReadOnly)
            .WithBindMount(mqscPath, "/etc/mqm/99-config.mqsc", AccessMode.ReadOnly)
            .WithPortBinding(1414, assignRandomHostPort: true)
            .WithPortBinding(9443, assignRandomHostPort: true)
            .WithNetwork(Network)
            .WithNetworkAliases(MqAlias)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1414))
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(9443))
            .Build();

        await Container.StartAsync();
        Port = Container.GetMappedPublicPort(1414);
        WebPort = Container.GetMappedPublicPort(9443);

        TrustCaOnHost(_caCert, Path.Combine(_trustDir, "ca.crt"));
        
        //await VerifyTlsFromClientContainerAsync();
    }

    private static void TrustCaOnHost(X509Certificate2 caCert, string caPemPath)
    {
        if (OperatingSystem.IsWindows())
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(caCert);
            store.Close();
        }
        else if (OperatingSystem.IsMacOS())
        {
            var loginKeychain = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library/Keychains/login.keychain-db");

            RunCli("security", $"add-trusted-cert -d -r trustRoot -k \"{loginKeychain}\" \"{caPemPath}\"");
        }
    }

    private static void UntrustCaOnHost(string caCommonName)
    {
        if (OperatingSystem.IsWindows())
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            foreach (var c in store.Certificates.Find(X509FindType.FindBySubjectName, caCommonName, false))
                store.Remove(c);
            store.Close();
        }
        else if (OperatingSystem.IsMacOS())
        {
            var loginKeychain = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library/Keychains/login.keychain-db");
            
            var res = RunCliCapture("security", $"find-certificate -a -Z -c \"{caCommonName}\" \"{loginKeychain}\"", ignoreErrors: true);
            var matches = Regex.Matches(res.stdout, @"SHA-1 hash:\s*([A-F0-9]{40})");

            foreach (Match m in matches)
            {
                var sha1 = m.Groups[1].Value;
                RunCli("security", $"delete-certificate -Z {sha1} \"{loginKeychain}\"", ignoreErrors: true);
            }
        }
        else
        {
            // Linux host: nulla da fare (non abbiamo toccato lo store).
        }
    }

    private static void RunCli(string fileName, string args, bool ignoreErrors = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (p.ExitCode != 0 && !ignoreErrors)
            throw new InvalidOperationException(
                $"{fileName} {args}\nExitCode={p.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    private static (int exitCode, string stdout, string stderr) RunCliCapture(string fileName, string args, bool ignoreErrors = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (p.ExitCode != 0 && !ignoreErrors)
            throw new InvalidOperationException(
                $"{fileName} {args}\nExitCode={p.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        return (p.ExitCode, stdout, stderr);
    }
    
    private async Task VerifyTlsFromClientContainerAsync()
    {
        var script = $@"
set -e
apt-get update
apt-get install -y --no-install-recommends ca-certificates openssl
cp /ca/ca.crt /usr/local/share/ca-certificates/testmq-ca.crt
update-ca-certificates
# Verifica TLS su MQ (1414) e WebConsole (9443) usando verify_return_error
echo '== TLS check MQ 1414 =='
openssl s_client -verify_return_error -connect host.docker.internal:{Port} -servername localhost </dev/null > /tmp/mq_tls.txt 2>&1 || (cat /tmp/mq_tls.txt; exit 1)
echo '== TLS check MQ Web 9443 =='
openssl s_client -verify_return_error -connect host.docker.internal:{WebPort} -servername localhost </dev/null > /tmp/web_tls.txt 2>&1 || (cat /tmp/web_tls.txt; exit 1)
echo 'OK'
";
        
        var client = new ContainerBuilder()
            .WithImage("debian:12-slim")
            .WithBindMount(_trustDir, "/ca", AccessMode.ReadOnly)
            .WithCommand("/bin/sh", "-lc", script)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("OK"))
            .Build();

        await client.StartAsync();
        await client.DisposeAsync();
    }
    
    public async Task DisposeAsync()
    {
        try
        {
            UntrustCaOnHost("TestMQ-CA");
        }
        catch { /* best effort */ }

        if (Container != null) await Container.DisposeAsync();
        
        if (Network != null) await Network.DisposeAsync();
        
        try { Directory.Delete(_workDir, recursive: true); } catch { /* ignore */ }
    }
    
    private static X509Certificate2 CreateCertificateAuthority(string cn)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        var ca = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));

        if (!ca.HasPrivateKey)
            ca = ca.CopyWithPrivateKey(rsa);

        return ca;
    }

    private static X509Certificate2 CreateServerCertificate(string dnsName, X509Certificate2 ca)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={dnsName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dnsName);
        req.CertificateExtensions.Add(san.Build());

        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // ServerAuth
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        using var caKey = ca.GetRSAPrivateKey()!;
        var serial = Guid.NewGuid().ToByteArray();
        var cert = req.Create(ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(3), serial);

        if (!cert.HasPrivateKey)
            cert = cert.CopyWithPrivateKey(rsa);

        return cert;
    }

    private static void WritePem(string path, string label, byte[] der)
    {
        var b64 = Convert.ToBase64String(der);

        using var w = new StreamWriter(path, false);

        w.WriteLine($"-----BEGIN {label}-----");

        for (int i = 0; i < b64.Length; i += 64)
        {
            w.WriteLine(b64.Substring(i, Math.Min(64, b64.Length - i)));
        }

        w.WriteLine($"-----END {label}-----");
    }

    private static void WriteMqPemFiles(string keysDir, string trustDir, X509Certificate2 caCert,
        X509Certificate2 serverCert)
    {
        Directory.CreateDirectory(keysDir);
        Directory.CreateDirectory(trustDir);
        
        WritePem(Path.Combine(trustDir, "ca.crt"), "CERTIFICATE", caCert.Export(X509ContentType.Cert));
        WritePem(Path.Combine(keysDir, "server.crt"), "CERTIFICATE", serverCert.Export(X509ContentType.Cert));
        
        using var rsa = serverCert.GetRSAPrivateKey()
                        ?? throw new InvalidOperationException(
                            "Il certificato server non ha una private key associata.");
        WritePem(Path.Combine(keysDir, "server.key"), "PRIVATE KEY", rsa.ExportPkcs8PrivateKey());
    }
}