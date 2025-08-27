using System;
using System.IO;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Xunit;
using Xunit.Abstractions;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class MqTlsEndToEndTlsContainerTests(MqTlsFixture fx, ITestOutputHelper logger) : IClassFixture<MqTlsFixture>
{
    [Fact]
    public void Should_forward_message_between_queues_with_tls_in_container()
    {
        RunPutGetFromLinuxDotnetContainer(fx, logger);
    }

    private static void RunPutGetFromLinuxDotnetContainer(MqTlsFixture fx, ITestOutputHelper log)
    {
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
            using System.IO;
            using System.Collections;
            using IBM.WMQ;
            using System.Security.Cryptography;
            using System.Security.Cryptography.X509Certificates;

            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadWrite);
                var certPem = File.ReadAllText("/ca/ca.crt");
                var cert = X509Certificate2.CreateFromPem(certPem);
                store.Add(cert);
            }

            string host = Environment.GetEnvironmentVariable("MQ_HOST")!;
            int port     = int.Parse(Environment.GetEnvironmentVariable("MQ_PORT")!);
            string chl   = Environment.GetEnvironmentVariable("MQ_CHANNEL")!;
            string qname = Environment.GetEnvironmentVariable("MQ_QUEUE")!;

            var props = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, host },
                { MQC.PORT_PROPERTY, port },
                { MQC.CHANNEL_PROPERTY, chl },
                { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
                { MQC.SSL_CIPHER_SPEC_PROPERTY, "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384" },
                { MQC.USER_ID_PROPERTY, "app" },
                { MQC.PASSWORD_PROPERTY, "passw0rd" }
            };

            try
            {
                using var qmgr = new MQQueueManager("", props);
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

        var script = $@"

set -e
apt-get update
apt-get install -y --no-install-recommends ca-certificates curl git
cp /ca/ca.crt /usr/local/share/ca-certificates/testmq-ca.crt
update-ca-certificates

cd /app
dotnet restore
dotnet run -c Release
";

        var client = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/dotnet/sdk:8.0")
            .WithBindMount(appDir, "/app", AccessMode.ReadWrite)
            .WithBindMount(fx.TrustDir, "/ca", AccessMode.ReadOnly)
            .WithEnvironment("MQ_HOST", "host.docker.internal")
            .WithEnvironment("MQ_PORT", fx.MqServer1.GetMappedPublicPort(1414).ToString())
            .WithEnvironment("MQ_CHANNEL", "APP.TLS.SVRCONN")
            .WithEnvironment("MQ_QUEUE", "DEV.QUEUE.1")
            .WithCommand("/bin/sh", "-lc", script)
            .WithNetwork(fx.Network)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("PUTGET_OK"))
            .Build();

        try
        {
            client.StartAsync().GetAwaiter().GetResult();
            log.WriteLine("Put/Get in container .NET → OK");
        }
        finally
        {
            client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            try
            {
                Directory.Delete(appDir, true);
            }
            catch
            {
                /* ignore */
            }
        }
    }
}