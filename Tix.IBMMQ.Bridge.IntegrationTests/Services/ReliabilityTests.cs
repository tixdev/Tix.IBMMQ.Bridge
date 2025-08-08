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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Logging;
using Tix.IBMMQ.Bridge.Options;
using Tix.IBMMQ.Bridge.Services;
using Xunit;
using Xunit.Abstractions;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class ReliabilityTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _logger;
    private readonly IContainer _mqServer1, _mqServer2;

    static ReliabilityTests()
    {
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    public ReliabilityTests(ITestOutputHelper logger)
    {
        _logger = logger;
        var isArm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        var image = isArm
            ? "ibm-mqadvanced-server-dev:9.4.3.0-arm64"
            : "ibmcom/mq:latest";

        if (isArm && !ImageExists(image))
        {
            RunScript("./build-arm-mq-image.sh");
        }

        var mqscPath = Path.GetFullPath("queues.mqsc");

        _mqServer1 = new ContainerBuilder()
            .WithImage(image)
            .WithEnvironment("LICENSE", "accept")
            .WithEnvironment("MQ_QMGR_NAME", "QM1")
            .WithEnvironment("MQ_APP_PASSWORD", "passw0rd")
            .WithEnvironment("MQ_ADMIN_PASSWORD", "passw0rd")
            .WithExposedPort(1414)
            .WithPortBinding(1414, true)
            .WithExposedPort(9443)
            .WithPortBinding(9443, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1414))
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(9443))
            .WithBindMount(mqscPath, "/etc/mqm/99-queues.mqsc", AccessMode.ReadOnly)
            .Build();

        _mqServer2 = new ContainerBuilder()
            .WithImage(image)
            .WithEnvironment("LICENSE", "accept")
            .WithEnvironment("MQ_QMGR_NAME", "QM1")
            .WithEnvironment("MQ_APP_PASSWORD", "passw0rd")
            .WithEnvironment("MQ_ADMIN_PASSWORD", "passw0rd")
            .WithExposedPort(1414)
            .WithPortBinding(1414, true)
            .WithExposedPort(9443)
            .WithPortBinding(9443, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1414))
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(9443))
            .WithBindMount(mqscPath, "/etc/mqm/99-queues.mqsc", AccessMode.ReadOnly)
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _mqServer1.StartAsync();
        await _mqServer2.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _mqServer1.DisposeAsync();
        await _mqServer2.DisposeAsync();
    }

    [Fact]
    public async Task Should_not_lose_messages_when_outbound_queue_is_full()
    {
        var server1Port = _mqServer1.GetMappedPublicPort(1414);
        var server2Port = _mqServer2.GetMappedPublicPort(1414);

        const string channel = "DEV.APP.SVRCONN";
        const string inboundQueueName = "RELIABILITY.IN";
        const string outboundQueueName = "RELIABILITY.OUT";

        var conn1 = new ConnectionOptions
        {
            QueueManagerName = "QM1",
            ConnectionName = $"localhost({server1Port})",
            UserId = "app",
            Password = "passw0rd"
        };
        var conn2 = new ConnectionOptions
        {
            QueueManagerName = "QM1",
            ConnectionName = $"localhost({server2Port})",
            UserId = "app",
            Password = "passw0rd"
        };

        // 1a. Clean the queues
        _logger.WriteLine($"Cleaning queues {inboundQueueName} and {outboundQueueName}");
        DrainQueue(conn1, channel, inboundQueueName);
        DrainQueue(conn2, channel, outboundQueueName);

        // 1b. Put 100 messages on the inbound queue
        _logger.WriteLine($"Putting 100 messages on {inboundQueueName}");
        for (var i = 0; i < 100; i++)
        {
            PutMessage(conn1, channel, inboundQueueName, $"Message {i}");
        }

        // 1c. Configure and run the bridge
        var options = CreateMqBridgeOptions(conn1, conn2, channel, inboundQueueName, outboundQueueName);
        _logger.WriteLine("Starting MQ Bridge Service");
        await RunMqBridge(options);

        // 1d. Wait for processing
        _logger.WriteLine("Waiting for messages to be processed...");
        await Task.Delay(TimeSpan.FromSeconds(20));

        // 1e. Verify final queue depths
        _logger.WriteLine("Verifying queue depths...");
        var inboundDepth = GetQueueDepth(conn1, channel, inboundQueueName);
        var outboundDepth = GetQueueDepth(conn2, channel, outboundQueueName);

        inboundDepth.ShouldBe(5);
        outboundDepth.ShouldBe(95);

        _logger.WriteLine("Test finished successfully.");
    }

    private static MQBridgeOptions CreateMqBridgeOptions(ConnectionOptions conn1, ConnectionOptions conn2,
        string channel, string inboundQueue, string outboundQueue)
    {
        return new MQBridgeOptions
        {
            Connections =
            {
                ["ConnA"] = conn1,
                ["ConnB"] = conn2
            },
            QueuePairs =
            [
                new()
                {
                    InboundConnection = "ConnA",
                    InboundChannel = channel,
                    InboundQueue = inboundQueue,
                    OutboundConnection = "ConnB",
                    OutboundChannel = channel,
                    OutboundQueue = outboundQueue,
                    PollIntervalSeconds = 1
                }
            ]
        };
    }

    private async Task RunMqBridge(MQBridgeOptions options)
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureHostOptions(o =>
            {
                o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new XunitLoggerProvider(_logger));
            })
            .ConfigureServices(s =>
            {
                s.AddSingleton<IHostedService, MQBridgeService>();
                s.Configure<MQBridgeOptions>(opts =>
                {
                    opts.Connections = options.Connections;
                    opts.QueuePairs = options.QueuePairs;
                });
            })
            .Build();

        await host.StartAsync();
    }

    private static void PutMessage(ConnectionOptions conn, string channel, string queueName, string message)
    {
        var props = BuildProperties(conn, channel);
        using var qMgr = new MQQueueManager(conn.QueueManagerName, props);
        using var queue = qMgr.AccessQueue(queueName, MQC.MQOO_OUTPUT | MQC.MQOO_FAIL_IF_QUIESCING);
        var mqMessage = new MQMessage();
        mqMessage.WriteString(message);
        queue.Put(mqMessage); // Not using syncpoint for test setup simplicity
    }

    private static int GetQueueDepth(ConnectionOptions conn, string channel, string queueName)
    {
        var props = BuildProperties(conn, channel);
        using var qMgr = new MQQueueManager(conn.QueueManagerName, props);
        using var queue = qMgr.AccessQueue(queueName, MQC.MQOO_INQUIRE | MQC.MQOO_FAIL_IF_QUIESCING);
        return queue.CurrentDepth;
    }

    private static void DrainQueue(ConnectionOptions conn, string channel, string queueName)
    {
        var props = BuildProperties(conn, channel);
        using var qMgr = new MQQueueManager(conn.QueueManagerName, props);
        using var queue = qMgr.AccessQueue(queueName, MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_FAIL_IF_QUIESCING);
        var gmo = new MQGetMessageOptions { Options = MQC.MQGMO_NO_WAIT | MQC.MQGMO_ACCEPT_TRUNCATED_MSG };

        while (true)
        {
            try
            {
                var msg = new MQMessage();
                queue.Get(msg, gmo);
            }
            catch (MQException ex) when (ex.Reason == MQC.MQRC_NO_MSG_AVAILABLE)
            {
                break; // Queue is empty
            }
        }
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
