using DotNet.Testcontainers;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using IBM.WMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using System.Collections;
using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using Tix.IBMMQ.Bridge.Options;
using Tix.IBMMQ.Bridge.Services;
using Xunit;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class MQBridgeIntegrationTests : IAsyncLifetime
{
    private readonly IContainer _container;

    // The Testcontainers resource reaper (Ryuk) cannot start under Podman
    // because Podman refuses to hijack a chunked stream. Disabling it
    // avoids 'cannot hijack chunked or content length stream' errors
    // when running the integration tests.
    static MQBridgeIntegrationTests()
    {
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    public MQBridgeIntegrationTests()
    {
        var isArm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        var image = isArm
            // Use the developer image built from the mq-container project when running
            // on Apple Silicon or other ARM64 machines
            ? "ibm-mqadvanced-server-dev:9.4.3.0-arm64"
            // Otherwise pull the official image from Docker Hub
            : "ibmcom/mq:latest";

        if (isArm && !ImageExists(image))
        {
            RunScript("./build-arm-mq-image.sh");
        }

        _container = new ContainerBuilder()
            .WithImage(image)
            .WithEnvironment("LICENSE", "accept")
            .WithEnvironment("MQ_QMGR_NAME", "QM1")
            .WithEnvironment("MQ_APP_PASSWORD", "passw0rd")
            .WithExposedPort(1414)
            .WithPortBinding(1414, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1414))
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var createQueues =
            "for i in $(seq 1 200); do echo \"DEFINE QLOCAL('DEV.QUEUE.$i') REPLACE\"; done | /opt/mqm/bin/runmqsc QM1";
        await _container.ExecAsync(new[] { "bash", "-c", createQueues });

        var setAuth =
            "/opt/mqm/bin/setmqaut -m QM1 -t queue -n DEV.QUEUE.* -p app +put +get +browse";
        await _container.ExecAsync(new[] { "bash", "-c", setAuth });
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Should_forward_message_between_queues()
    {
        var port = _container.GetMappedPublicPort(1414);
        const string channel = "DEV.APP.SVRCONN";
        var conn = new ConnectionOptions
        {
            QueueManagerName = "QM1",
            ConnectionName = $"localhost({port})",
            UserId = "app",
            Password = "passw0rd"
        };

        var options = new MQBridgeOptions
        {
            Connections =
            {
                ["ConnA"] = conn,
                ["ConnB"] = conn
            }
        };

        for (var i = 0; i < 100; i++)
        {
            options.QueuePairs.Add(new QueuePairOptions
            {
                InboundConnection = "ConnA",
                InboundChannel = channel,
                InboundQueue = $"DEV.QUEUE.{2 * i + 1}",
                OutboundConnection = "ConnB",
                OutboundChannel = channel,
                OutboundQueue = $"DEV.QUEUE.{2 * i + 2}",
                PollIntervalSeconds = 1
            });
        }

        PutMessage(conn, channel, "DEV.QUEUE.1", "hello");

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddLogging();
                s.AddSingleton<IHostedService, MQBridgeService>();
                s.Configure<MQBridgeOptions>(opts =>
                {
                    opts.Connections = options.Connections;
                    opts.QueuePairs = options.QueuePairs;
                });
            })
            .Build();

        await host.StartAsync();
        await Task.Delay(2000);
        await host.StopAsync();

        var message = GetMessage(conn, channel, "DEV.QUEUE.2");
        message.ShouldBe("hello");
    }

    private static void PutMessage(ConnectionOptions conn, string channel, string queueName, string message)
    {
        var props = BuildProperties(conn, channel);
        using var qMgr = new MQQueueManager(conn.QueueManagerName, props);
        using var queue = qMgr.AccessQueue(queueName, MQC.MQOO_OUTPUT | MQC.MQOO_FAIL_IF_QUIESCING);
        var mqMessage = new MQMessage();
        mqMessage.WriteString(message);
        queue.Put(mqMessage, new MQPutMessageOptions { Options = MQC.MQPMO_SYNCPOINT });
        qMgr.Commit();
    }

    private static string GetMessage(ConnectionOptions conn, string channel, string queueName)
    {
        var props = BuildProperties(conn, channel);
        using var qMgr = new MQQueueManager(conn.QueueManagerName, props);
        using var queue = qMgr.AccessQueue(queueName, MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_FAIL_IF_QUIESCING);
        var msg = new MQMessage();
        queue.Get(msg, new MQGetMessageOptions { Options = MQC.MQGMO_NO_WAIT });
        return msg.ReadString(msg.DataLength);
    }

    private static Hashtable BuildProperties(ConnectionOptions opts, string channel)
    {
        var (host, port) = MQBridgeService.ParseConnectionName(opts.ConnectionName);
        return new Hashtable
        {
            { MQC.HOST_NAME_PROPERTY, host },
            { MQC.PORT_PROPERTY, port },
            { MQC.CHANNEL_PROPERTY, channel },
            { MQC.USER_ID_PROPERTY, opts.UserId },
            { MQC.PASSWORD_PROPERTY, opts.Password },
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED }
        };
    }

    private static bool ImageExists(string image)
    {
        var psi = new ProcessStartInfo("docker", $"image inspect {image}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi);
        proc.WaitForExit();
        return proc.ExitCode == 0;
    }

    private static void RunScript(string script)
    {
        var psi = new ProcessStartInfo("bash", script)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi);
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"Script {script} failed.");
        }
    }
}
