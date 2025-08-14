using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Tix.IBMMQ.Bridge.E2ETests.Helpers;
using Tix.IBMMQ.Bridge.Options;
using Xunit;

namespace Tix.IBMMQ.Bridge.E2ETests
{
    public class ReliabilityE2ETestsFixture : IAsyncLifetime
    {
        private BridgeContainer _bridgeContainer;
        public MqE2EOperations MqInOps { get; private set; }
        public MqE2EOperations MqOutOps { get; private set; }
        public ConnectionOptions ConnIn { get; private set; }
        public ConnectionOptions ConnOut { get; private set; }
        public string InboundChannel { get; private set; }
        public string InboundQueue { get; private set; }
        public string OutboundChannel { get; private set; }
        public string OutboundQueue { get; private set; }

        public async Task InitializeAsync()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            ConnIn = configuration.GetSection("MQBridge:Connections:ConnA").Get<ConnectionOptions>();
            ConnOut = configuration.GetSection("MQBridge:Connections:ConnB").Get<ConnectionOptions>();

            var queuePair = configuration.GetSection("MQBridge:QueuePairs:0").Get<QueuePairOptions>();
            InboundChannel = queuePair.InboundChannel;
            InboundQueue = queuePair.InboundQueue;
            OutboundChannel = queuePair.OutboundChannel;
            OutboundQueue = queuePair.OutboundQueue;

            MqInOps = new MqE2EOperations(ConnIn);
            MqOutOps = new MqE2EOperations(ConnOut);

            _bridgeContainer = new BridgeContainer();
            await _bridgeContainer.StartAsync();
        }

        public async Task DisposeAsync()
        {
            if (_bridgeContainer != null)
            {
                await _bridgeContainer.DisposeAsync();
            }
        }
    }
}
