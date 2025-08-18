using IBM.WMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using System.Collections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tix.IBMMQ.Bridge.Options;
using Tix.IBMMQ.Bridge.Services;
using Xunit;
using Xunit.Abstractions;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class MQBridgeIntegrationTlsTests(MqTlsFixture fx, ITestOutputHelper logger) : IClassFixture<MqTlsFixture>
{
    [Fact]
    public async Task Should_forward_message_between_queues()
    {
        var server1Port = fx.MqServer1.GetMappedPublicPort(1414);
        var server2Port = fx.MqServer2.GetMappedPublicPort(1414);

        var server1WebPort = fx.MqServer1.GetMappedPublicPort(9443);
        var server2WebPort = fx.MqServer2.GetMappedPublicPort(9443);

        logger.WriteLine($"Server1 clientport {server1Port} - web https://localhost:{server1WebPort}");
        logger.WriteLine($"Server2 clientport {server2Port} - web https://localhost:{server2WebPort}");

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

        logger.WriteLine("Starting bridge");
        await RunMqBridge(options);

        var rnd = new Random();

        var tasks = new List<Task>();
        
        var messagesCount = 1000000;
        
        for (int i = 0; i < messagesCount; i++)
        {
            var qp = options.QueuePairs[rnd.Next(1, options.QueuePairs.Count)];
            var task = Task.Run(() =>
            {
                PutMessage(conn1, channel, qp.InboundQueue, "hello");
                logger.WriteLine($"Putting message on Server1 queue: {qp.InboundQueue}");
            });

            tasks.Add(task);
            
            await Task.Delay(TimeSpan.FromMilliseconds(50));   
        }
        
        Task.WaitAll(tasks.ToArray());
        
        await Task.Delay(TimeSpan.FromSeconds(10));
        
        var messagesReceived = 0;
        
        options.QueuePairs.ForEach(qp =>
        {
            logger.WriteLine($"Getting messages from Server2 queue: {qp.OutboundQueue}");

            try
            {
                while (true)
                {
                    var message = GetMessage(conn2, channel, qp.OutboundQueue);
                    Interlocked.Increment(ref messagesReceived);
                    message.ShouldBe("hello");
                }
            }
            catch {}
        });
        
        messagesReceived.ShouldBeEquivalentTo(messagesCount);
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

        Enumerable.Range(1, 800).Select(n => n).ToList().ForEach(n =>
        {
            options.QueuePairs.Add(new QueuePairOptions
            {
                InboundConnection = "ConnA",
                InboundChannel = channel,
                InboundQueue = $"DEV.QUEUE.{n}",
                OutboundConnection = "ConnB",
                OutboundChannel = channel,
                OutboundQueue = $"DEV.QUEUE.{n}",
                PollIntervalSeconds = 30
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
                logging.AddProvider(new XunitLoggerProvider(logger));
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
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
            { MQC.SSL_CIPHER_SPEC_PROPERTY, "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384" }
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