using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tix.IBMMQ.Bridge.Options;
using Tix.IBMMQ.Bridge.Services;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Helpers
{
    public class MqBridgeHost
    {
        private IHost _host;
        private MQBridgeOptions _options = new MQBridgeOptions();
        private readonly ITestOutputHelper _logger;

        public MqBridgeHost(ITestOutputHelper logger)
        { 
            _logger = logger;
        }

        public void SetConnections(ConnectionOptions inbound, ConnectionOptions outbound)
        {
            _options.Connections.Add("ConnIn", inbound);
            _options.Connections.Add("ConnOut", outbound);
        }

        public void AddQueuePair(string channel, string queue) =>
            AddQueuePair(channel, queue, queue);

        public void AddQueuePair(string channel, string inQueue, string outQueue)
        {
            _options.QueuePairs.Add(new() 
            {
                InboundConnection = "ConnIn", 
                InboundChannel = channel, 
                InboundQueue = inQueue,
                OutboundConnection = "ConnOut",
                OutboundChannel = channel,
                OutboundQueue = outQueue,
            });
        }

        public async Task RestartAsync(CancellationToken cancellationToken = default)
        {
            if (_host != null)
                await StopAsync(cancellationToken);

            await StartAsync(cancellationToken);
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_host != null)
                return; // Bridge has already started, do nothing or use Restart instead

            _logger.WriteLine("Starting MQ Bridge Service...");

            _options.Validate();

            _host = Host.CreateDefaultBuilder()
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
                        opts.Connections = _options.Connections;
                        opts.QueuePairs = _options.QueuePairs;
                    });
                })
                .Build();

            await _host.StartAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_host != null)
            {
                await _host.StopAsync(cancellationToken);
                _host.Dispose();
                _host = null;
            }
        }
    }
}
