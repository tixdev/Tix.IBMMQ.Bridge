using IBM.WMQ;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Tix.IBMMQ.Bridge.Options;

namespace Tix.IBMMQ.Bridge.Services;

public class MQBridgeService : BackgroundService
{
    private readonly ILogger<MQBridgeService> _logger;
    private readonly MQBridgeOptions _options;

    public MQBridgeService(IOptions<MQBridgeOptions> options, ILogger<MQBridgeService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = _options.QueuePairs
            .Select(pair =>
                Task.Factory
                    .StartNew(
                        () => ProcessPairAsync(pair, stoppingToken),
                        stoppingToken,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default
                    )
                    .Unwrap()
            );
        
        return Task.WhenAll(tasks);
    }

    private async Task ProcessPairAsync(QueuePairOptions pair, CancellationToken token)
    {
        while (true)
        {
            try
            {
                var inbound = _options.Connections[pair.InboundConnection];
                var outbound = _options.Connections[pair.OutboundConnection];

                using var inboundQMgr = new MQQueueManager(inbound.QueueManagerName, BuildProperties(inbound, pair.InboundChannel));
                using var outboundQMgr = new MQQueueManager(outbound.QueueManagerName, BuildProperties(outbound, pair.OutboundChannel));

                using var inboundQueue = inboundQMgr.AccessQueue(pair.InboundQueue, MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_FAIL_IF_QUIESCING);
                using var outboundQueue = outboundQMgr.AccessQueue(pair.OutboundQueue, MQC.MQOO_OUTPUT | MQC.MQOO_FAIL_IF_QUIESCING);

                var gmo = new MQGetMessageOptions
                {
                    Options = MQC.MQGMO_WAIT | MQC.MQGMO_SYNCPOINT,
                    WaitInterval = pair.PollIntervalSeconds * 1000
                };
                
                var pmo = new MQPutMessageOptions { Options = MQC.MQPMO_SYNCPOINT };

                while (true)
                {
                    //must be insert logic to reconnect every x cycles without messages
                    var message = new MQMessage();
                    try
                    {
                        _logger.LogInformation("Getting message on {Inbound}", pair.InboundQueue);
                        inboundQueue.Get(message, gmo);
                        _logger.LogInformation("Received message from {Inbound}", pair.InboundQueue);
                        outboundQueue.Put(message, pmo);
                        inboundQMgr.Commit();
                        outboundQMgr.Commit();
                        _logger.LogInformation("Forwarded message to {Outbound}", pair.OutboundQueue);
                    }
                    catch (MQException ex) when (ex.Reason == MQC.MQRC_NO_MSG_AVAILABLE)
                    {
                        _logger.LogInformation("No message available on {Inbound}", pair.InboundQueue);
                    }
                    catch (Exception)
                    {
                        inboundQMgr.Backout();
                        outboundQMgr.Backout();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pair {Inbound}->{Outbound}", pair.InboundQueue, pair.OutboundQueue);
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }

    private Hashtable BuildProperties(ConnectionOptions opts, string channel)
    {
        var (host, port) = ParseConnectionName(opts.ConnectionName);
        return new Hashtable
        {
            { MQC.HOST_NAME_PROPERTY, host },
            { MQC.PORT_PROPERTY, port },
            { MQC.CHANNEL_PROPERTY, channel },
            { MQC.USER_ID_PROPERTY, opts.UserId },
            { MQC.PASSWORD_PROPERTY, opts.Password },
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
            { MQC.SSL_CIPHER_SPEC_PROPERTY, "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384" },
            { MQC.APPNAME_PROPERTY, "Tix.IBMMQ.Bridge" }
        };
    }

    public static (string host, int port) ParseConnectionName(string connectionName)
    {
        var start = connectionName.IndexOf('(');
        var end = connectionName.IndexOf(')', start + 1);
        var host = connectionName.Substring(0, start);
        var portStr = connectionName.Substring(start + 1, end - start - 1);
        return (host, int.Parse(portStr));
    }
}
