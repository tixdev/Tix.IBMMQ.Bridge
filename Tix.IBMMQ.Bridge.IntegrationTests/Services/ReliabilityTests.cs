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
using Tix.IBMMQ.Bridge.IntegrationTests.Helpers;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class ReliabilityTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _logger;
    private readonly MqContainer _mqServerIn, _mqServerOut;

    static ReliabilityTests()
    {
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    public ReliabilityTests(ITestOutputHelper logger)
    {
        _logger = logger;
        var imageIn = new ContainerImage(/*old ver*/); // TODO: usare la versione 9.1 per il server di Inbound, diversa da Outbound
        var imageOut = new ContainerImage(); 

        var mqscPath = Path.GetFullPath("reliability-test-queues.mqsc");
        _mqServerIn = imageIn.BuildMqContainer(mqStartupScriptPath: mqscPath);
        _mqServerOut = imageOut.BuildMqContainer(mqStartupScriptPath: mqscPath);
    }

    public async Task InitializeAsync()
    {
        await _mqServerIn.InitializeAsync();
        await _mqServerOut.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _mqServerIn.DisposeAsync();
        await _mqServerOut.DisposeAsync();
    }

    [Fact]
    public async Task Should_not_lose_messages_when_outbound_queue_is_full()
    {
        const string channel = "DEV.APP.SVRCONN";
        const string inboundQueueName = "RELIABILITY.IN";
        const string outboundQueueName = "RELIABILITY.OUT";

        var connIn = _mqServerIn.GetMqConnectionOptions();
        var connOut = _mqServerOut.GetMqConnectionOptions();

        // 1a. Clean the queues
        _logger.WriteLine($"Cleaning queues {inboundQueueName} and {outboundQueueName}");
        connIn.DrainQueue(channel, inboundQueueName);
        connOut.DrainQueue(channel, outboundQueueName);

        // 1b. Put 100 messages on the inbound queue
        _logger.WriteLine($"Putting 100 messages on {inboundQueueName}");
        for (var i = 0; i < 100; i++)
            connIn.PutMessage(channel, inboundQueueName, $"Message {i}");

        // 1c. Configure and run the bridge
        var options = CreateMqBridgeOptions(connIn, connOut, channel, inboundQueueName, outboundQueueName);
        _logger.WriteLine("Starting MQ Bridge Service and wait ");
        await RunWaitStopMqBridge(options, 3);

        // 1e. Verify final queue depths
        _logger.WriteLine("Verifying queue depths...");
        connIn.GetQueueDepth(channel, inboundQueueName).ShouldBe(5);
        connOut.GetQueueDepth(channel, outboundQueueName).ShouldBe(95);

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

    private async Task RunWaitStopMqBridge(MQBridgeOptions options, int waitSeconds)
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

        await Task.Delay(TimeSpan.FromSeconds(waitSeconds));

        await host.StopAsync();
    }
}
