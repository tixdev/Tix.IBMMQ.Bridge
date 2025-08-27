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

public class MQBridgeIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _logger;
    private readonly IContainer _mqServer1, _mqServer2;

    // The Testcontainers resource reaper (Ryuk) cannot start under Podman
    // because Podman refuses to hijack a chunked stream. Disabling it
    // avoids 'cannot hijack chunked or content length stream' errors
    // when running the integration tests.
    static MQBridgeIntegrationTests()
    {
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    public MQBridgeIntegrationTests(ITestOutputHelper logger)
    {
        _logger = logger;
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
    public async Task Should_forward_message_between_queues()
    {
        var server1Port = _mqServer1.GetMappedPublicPort(1414);
        var server2Port = _mqServer2.GetMappedPublicPort(1414);

        var server1WebPort = _mqServer1.GetMappedPublicPort(9443);
        var server2WebPort = _mqServer2.GetMappedPublicPort(9443);

        _logger.WriteLine($"Server1 clientport {server1Port} - web https://localhost:{server1WebPort}");
        _logger.WriteLine($"Server2 clientport {server2Port} - web https://localhost:{server2WebPort}");

        const string channel = "DEV.APP.SVRCONN";
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

        var options = CreateMqBridgeOptions(conn1, conn2, channel);

        options.QueuePairs.ForEach(qp =>
        {
            _logger.WriteLine($"Putting message on Server1 queue: {qp.InboundQueue}");
            PutMessage(conn1, channel, qp.InboundQueue, "hello");
        });

        _logger.WriteLine("Start bridge");
        await RunMqBridge(options);
        
        await Task.Delay(TimeSpan.FromSeconds(10));
        
        options.QueuePairs.ForEach(qp =>
        {
            _logger.WriteLine($"Getting message from Server2 queue: {qp.OutboundQueue}");
            
            var message = GetMessage(conn2, channel, qp.OutboundQueue);
            message.ShouldBe("hello");
        });
        
        //await Task.Delay(TimeSpan.FromMinutes(10));
    }

    private static MQBridgeOptions CreateMqBridgeOptions(ConnectionOptions conn1, ConnectionOptions conn2,
        string channel)
    {
        var options = new MQBridgeOptions
        {
            Connections =
            {
                ["ConnA"] = conn1,
                ["ConnB"] = conn2
            },
            QueuePairs = []
        };

        Enumerable.Range(1, 200).Select(n => n).ToList().ForEach(n =>
        {
            options.QueuePairs.Add(new QueuePairOptions
            {
                InboundConnection = "ConnA",
                InboundChannel = channel,
                InboundQueue = $"DEV.QUEUE.{n}",
                OutboundConnection = "ConnB",
                OutboundChannel = channel,
                OutboundQueue = $"DEV.QUEUE.{n}",
                PollIntervalSeconds = 1
            });
        });

        return options;
    }

    private async Task RunMqBridge(MQBridgeOptions options)
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureHostOptions(o =>
            {
                o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
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
