using System;
using System.Collections;
using System.IO;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using IBM.WMQ;
using Xunit;
using Xunit.Abstractions;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class MqTlsEndToEndContainerTests(MqTlsFixture fx, ITestOutputHelper logger) : IClassFixture<MqTlsFixture>
{
    [Fact(DisplayName = "Put/Get via TLS (managed .NET client)", Skip = "Integration Test")]
    public void PutGet_Tls_Works()
    {
        var webPort = fx.Container.GetMappedPublicPort(9443);
        logger.WriteLine($"MQ Console → https://localhost:{webPort}/ibmmq/console/");

        if (OperatingSystem.IsMacOS())
        {
            RunPutGetFromLinuxDotnetContainer(fx, logger);
            return;
        }
        
        var props = new Hashtable
        {
            { MQC.HOST_NAME_PROPERTY, fx.Host },
            { MQC.PORT_PROPERTY, fx.Port },
            { MQC.CHANNEL_PROPERTY, "APP.TLS.SVRCONN" },
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT },

            // Per il managed client: impostare SSLCipherSpec indica che la connessione è TLS.
            // Il mapping reale dei cipher è gestito dallo stack TLS della piattaforma. :contentReference[oaicite:1]{index=1}
            { MQC.SSL_CIPHER_SPEC_PROPERTY, "TLS_RSA_WITH_AES_128_CBC_SHA256" }
        };

        using var qmgr = new MQQueueManager("QM1", props);
        using var queue = qmgr.AccessQueue("DEV.QUEUE.1", MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_OUTPUT);

        var put = new MQMessage(); put.WriteString("hello tls");
        queue.Put(put, new MQPutMessageOptions());

        var got = new MQMessage();
        var gmo = new MQGetMessageOptions { Options = MQC.MQGMO_WAIT, WaitInterval = 5000 };
        queue.Get(got, gmo);
        var body = got.ReadString(got.MessageLength);

        Assert.Equal("hello tls", body);
    }

    private static void RunPutGetFromLinuxDotnetContainer(MqTlsFixture fx, ITestOutputHelper log)
    {
        // Prepariamo una mini app .NET che fa Put/Get con IBMMQDotnetClient
        var appDir = Path.Combine(Path.GetTempPath(), "mq-putget-app-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(appDir);

        File.WriteAllText(Path.Combine(appDir, "mq-putget.csproj"),
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net8.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="IBMMQDotnetClient" Version="9.4.3" />
          </ItemGroup>
        </Project>
        """);

        File.WriteAllText(Path.Combine(appDir, "Program.cs"),
        """
        using System;
        using System.Collections;
        using IBM.WMQ;

        string host = Environment.GetEnvironmentVariable("MQ_HOST")!;
        int port     = int.Parse(Environment.GetEnvironmentVariable("MQ_PORT")!);
        string chl   = Environment.GetEnvironmentVariable("MQ_CHANNEL")!;
        string qname = Environment.GetEnvironmentVariable("MQ_QUEUE")!;

        var props = new Hashtable
        {
            { MQC.HOST_NAME_PROPERTY, host },
            { MQC.PORT_PROPERTY, port },
            { MQC.CHANNEL_PROPERTY, chl },
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT },
            // Indica al managed client che la connessione è TLS (il mapping dei cipher lo fa l'OS)
            { MQC.SSL_CIPHER_SPEC_PROPERTY, "TLS_RSA_WITH_AES_128_CBC_SHA256" }
        };

        try
        {
            using var qmgr = new MQQueueManager("QM1", props);
            using var queue = qmgr.AccessQueue(qname, MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_OUTPUT);
        
            var put = new MQMessage(); put.WriteString("hello tls");
            queue.Put(put, new MQPutMessageOptions());
        
            var got = new MQMessage();
            var gmo = new MQGetMessageOptions { Options = MQC.MQGMO_WAIT, WaitInterval = 5000 };
            queue.Get(got, gmo);
            var body = got.ReadString(got.MessageLength);
        
            if (body != "hello tls") throw new Exception($"Body mismatch: {body}");
            Console.WriteLine("PUTGET_OK");
        }
        catch (MQException mqe)
        {
            Console.WriteLine($"MQE CompCode={mqe.CompCode} Reason={mqe.Reason} Message={mqe.Message}");
            if (mqe.InnerException != null) Console.WriteLine("Inner: " + mqe.InnerException);
            throw;
        }
        """);

        // Script che gira nel container:
        // - installa ca-certificates
        // - copia la CA nel trust store del container
        // - restore+run della mini app
        var script = $@"

echo ""Arch container:""
uname -m        # atteso: x86_64
dotnet --info | sed -n '1,20p'

set -e
apt-get update
apt-get install -y --no-install-recommends ca-certificates curl git
cp /ca/ca.crt /usr/local/share/ca-certificates/testmq-ca.crt
update-ca-certificates
HOST=""$MQ_ALIAS""
PORT=""$MQ_PORT_INTERNAL""   # 1414 interno

echo '== DNS check (alias) =='
if getent hosts ""$HOST"" > /dev/null 2>&1; then
  echo ""Alias '$HOST' OK (rete condivisa).""
else
  echo ""Alias '$HOST' NON risolvibile → fallback host.docker.internal""
  HOST=""host.docker.internal""
  PORT=""$MQ_HOSTPORT_FALLBACK""    # porta host (fx.Port)
fi

echo ""Target finale: $HOST:$PORT""

echo '== TLS handshake check =='
set +e
openssl s_client -verify_return_error -connect ""$HOST:$PORT"" -servername localhost </dev/null > /tmp/mq_tls.txt 2>&1
rc=$?
set -e
head -n 50 /tmp/mq_tls.txt || true
[ $rc -eq 0 ] || {{ echo 'OpenSSL TLS check FAILED'; cat /tmp/mq_tls.txt; exit 1; }}

cd /app
dotnet restore
dotnet run -c Release
";
        
        var client = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/dotnet/sdk:8.0")
            .WithBindMount(appDir, "/app", AccessMode.ReadWrite)
            .WithBindMount(fx.TrustDir, "/ca", AccessMode.ReadOnly)          
            .WithEnvironment("MQ_HOST", "host.docker.internal")
            .WithEnvironment("MQ_PORT", fx.Port.ToString())
            .WithEnvironment("MQ_CHANNEL", "APP.TLS.SVRCONN")
            .WithEnvironment("MQ_QUEUE", "DEV.QUEUE.1")
            .WithCommand("/bin/sh", "-lc", script)
            .WithNetwork(fx.Network)
            .WithEnvironment("MQ_ALIAS", fx.MqAlias)   
            .WithEnvironment("MQ_PORT", fx.MqInternalPort.ToString())
            .WithEnvironment("MQ_PORT_INTERNAL", fx.MqInternalPort.ToString()) // "1414"
            .WithEnvironment("MQ_HOSTPORT_FALLBACK", fx.Port.ToString())
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("PUTGET_OK"))
            .Build();

        try
        {
            client.StartAsync().GetAwaiter().GetResult();
            log.WriteLine("Put/Get eseguito dal container .NET → OK");
        }
        finally
        {
            client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            try { Directory.Delete(appDir, true); } catch { /* ignore */ }
        }
    }
}
