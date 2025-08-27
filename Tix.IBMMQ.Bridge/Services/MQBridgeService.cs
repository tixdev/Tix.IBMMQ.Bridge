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
using System.Collections.Generic;

namespace Tix.IBMMQ.Bridge.Services;

public class MQBridgeService : BackgroundService
{
    private readonly ILogger<MQBridgeService> _logger;
    private readonly MQBridgeOptions _options;

    private static readonly List<int> retryDelaySequenceMs =
    // Retry strategy: set here the min and max seconds to wait after an error. It builds a time sequence
#if DEBUG
    GetRetryDelaySequence(1, 5);
#else
    GetRetryDelaySequence(5, 1800);
#endif

    private static readonly (int Min, int Max) mqWaitIntervalRangeSec = (30, 60);

    public MQBridgeService(IOptions<MQBridgeOptions> options, ILogger<MQBridgeService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.Register(() =>
            _logger.LogInformation("Cancellation requested!")
        );

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
        int delaysAfterError = retryDelaySequenceMs[0];

        var inbound = _options.Connections[pair.InboundConnection];
        var outbound = _options.Connections[pair.OutboundConnection];

        _logger.LogInformation("{from} > {to}: {queue}", 
            inbound.ConnectionName, outbound.ConnectionName,
            pair.InboundQueue + (pair.InboundQueue != pair.OutboundQueue ? $" > {pair.OutboundQueue}" : null)
            );

        // Holds the last successfully forwarded MessageId to avoid duplicates after a crash/outage
        byte[] lastMessageId = Array.Empty<byte>();

        while (!token.IsCancellationRequested)
        {
            try
            {
                using var inboundQMgr = new MQQueueManager(inbound.QueueManagerName, BuildProperties(inbound, pair.InboundChannel));
                using var outboundQMgr = new MQQueueManager(outbound.QueueManagerName, BuildProperties(outbound, pair.OutboundChannel));

                using var inboundQueue = inboundQMgr.AccessQueue(pair.InboundQueue, MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_FAIL_IF_QUIESCING);
                using var outboundQueue = outboundQMgr.AccessQueue(pair.OutboundQueue, MQC.MQOO_OUTPUT | MQC.MQOO_FAIL_IF_QUIESCING);

                var gmo = new MQGetMessageOptions
                {
                    Options = MQC.MQGMO_WAIT | MQC.MQGMO_SYNCPOINT,
                    WaitInterval = Random.Shared.Next(mqWaitIntervalRangeSec.Min * 1000, mqWaitIntervalRangeSec.Max * 1000)
                };

                var pmo = new MQPutMessageOptions { Options = MQC.MQPMO_SYNCPOINT };

                /// Note: 
                /// - we are in a sequential processing context
                /// - outboundQMgr.Commit() before inboundQMgr.Commit() grants at-least once delivery
                /// - in case of outbound server outages, no message will be transmitted
                /// - in case of inbound server outages, lastMessageId check avoid message duplication
                /// So, this pattern grants exactly-once delivery
                while (!token.IsCancellationRequested)
                {
                    var message = new MQMessage();

                    try
                    {
                        inboundQueue.Get(message, gmo);
                        _logger.LogInformation("Received message from {Inbound}", pair.InboundQueue);
                        if (message.MessageId.SequenceEqual(lastMessageId))
                        {
                            _logger.LogInformation("Skipping duplicate message with MessageId {MessageId}", BitConverter.ToString(message.MessageId));
                            inboundQMgr.Commit(); // Still remove it from the queue
                            continue;
                        }

                        outboundQueue.Put(message, pmo);
                        outboundQMgr.Commit();
                        lastMessageId = message.MessageId.ToArray();
                        _logger.LogInformation("Forwarded message to {Outbound}", pair.OutboundQueue);

                        inboundQMgr.Commit();
                    }
                    catch (MQException mqEx) when (mqEx.Reason == MQC.MQRC_NO_MSG_AVAILABLE)
                    {
                        break;
                    }
                    catch
                    {
                        if (inboundQMgr.IsConnected)
                            inboundQMgr.Backout();

                        if (outboundQMgr.IsConnected)
                            outboundQMgr.Backout();

                        throw;
                    }
                }

                delaysAfterError = retryDelaySequenceMs[0];
            }
            catch (Exception ex)
            {
                if (delaysAfterError == retryDelaySequenceMs.Last())
                    _logger.LogError(ex, "Error processing pair {Inbound}->{Outbound}: {exc}", pair.InboundQueue, pair.OutboundQueue, ex.Message);
                else
                    _logger.LogWarning(ex, "Error processing pair {Inbound}->{Outbound}: {exc}. Retry in {delay} ms", pair.InboundQueue, pair.OutboundQueue, ex.Message, delaysAfterError);

                if (!token.IsCancellationRequested)
                {
                    await Task.Delay(delaysAfterError, token);

                    int nextIdx = retryDelaySequenceMs.FindIndex(x => x > delaysAfterError);
                    if (nextIdx >= 0)
                        delaysAfterError = retryDelaySequenceMs[nextIdx];
                }
            }
        }
    }

    /// Es GetRetryDelaySequenceMs(5, 1800): wait between 5 to 1800 sec (30 min)
    /// Result sequence in seconds:
    /// 5 > 7.03 > 14.06 > 28.12 > 56.25 > 112.5 > 225 > 450 > 900 > 1800
    static List<int> GetRetryDelaySequence(int minSeconds, int maxSeconds)
    {
        var delays = new List<int>();
        int next = maxSeconds;
        while (next > minSeconds)
        {
            delays.Add(next * 1000); // Convert in ms
            next /= 2;
        }

        delays.Add(minSeconds * 1000); // Start from min
        delays.Reverse();

        return delays;
    }

    private Hashtable BuildProperties(ConnectionOptions opts, string channel)
    {
        var (host, port) = ParseConnectionName(opts.ConnectionName);
        var properties = new Hashtable
        {
            { MQC.HOST_NAME_PROPERTY, host },
            { MQC.PORT_PROPERTY, port },
            { MQC.CHANNEL_PROPERTY, channel },
            { MQC.USER_ID_PROPERTY, opts.UserId },
            { MQC.PASSWORD_PROPERTY, opts.Password },
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED }
        };

        if (opts.UseTls)
        {
            if (string.IsNullOrEmpty(opts.SslCipherSpec))
                throw new InvalidOperationException("No SSL Cipher Spec specified: use SslCipherSpec connection property");
             
            properties.Add(MQC.SSL_CIPHER_SPEC_PROPERTY, opts.SslCipherSpec);
        }

        return properties;
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
