using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Shouldly;
using Tix.IBMMQ.Bridge.E2ETests.Helpers;
using Xunit;

namespace Tix.IBMMQ.Bridge.E2ETests
{
    public class ReliabilityE2ETests : IClassFixture<ReliabilityE2ETestsFixture>
    {
        private readonly ReliabilityE2ETestsFixture _fixture;

        public ReliabilityE2ETests(ReliabilityE2ETestsFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void Should_be_able_to_reach_both_mq_servers()
        {
            // As per user instructions, this test will be run by them.
            // This test will fail if the servers are not configured correctly.
            var inReachable = _fixture.MqInOps.IsReachable(_fixture.InboundChannel);
            var outReachable = _fixture.MqOutOps.IsReachable(_fixture.OutboundChannel);

            inReachable.ShouldBeTrue("The inbound MQ server is not reachable. Please check the configuration in appsettings.json.");
            outReachable.ShouldBeTrue("The outbound MQ server is not reachable. Please check the configuration in appsettings.json.");
        }

        [Fact]
        public void Should_transfer_message_from_source_to_destination()
        {
            var correlationId = Guid.NewGuid().ToString();
            var messageText = $"E2E-Test-{correlationId}";

            _fixture.MqInOps.PutMessage(_fixture.InboundChannel, _fixture.InboundQueue, messageText, correlationId);

            _fixture.MqOutOps.GetMessage(_fixture.OutboundChannel, _fixture.OutboundQueue, 5000, correlationId)
                .ShouldNotBeNull("Message was not received at the destination queue within the 5-second timeout.")
                .ShouldBe(messageText);
        }
    }
}
