using System;
using Shouldly;
using Tix.IBMMQ.Bridge.E2ETests.Helpers;
using Xunit;
using Xunit.Abstractions;

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
            var pair0 = _fixture.QueuePairs[0];
            _fixture.ConnIn.IsReachable(pair0.InboundChannel)
                .ShouldBeTrue("The inbound MQ server is not reachable. Please check the configuration in appsettings.");
            _fixture.ConnOut.IsReachable(pair0.OutboundChannel)
                .ShouldBeTrue("The outbound MQ server is not reachable. Please check the configuration in appsettings.");
        }

        [Fact]
        public void Should_transfer_message_from_source_to_destination()
        {
            var correlationId = Guid.NewGuid().ToString();
            var messageText = $"E2E-Test-{correlationId}";
            foreach (var pair in _fixture.QueuePairs)
            {
                Action put = () =>
                    _fixture.ConnIn.PutMessage(pair.InboundChannel, pair.InboundQueue, messageText, correlationId);
                put.ShouldNotThrow($"Message not sent to inbound queue {pair.InboundQueue}");

                _fixture.ConnOut.GetMessage(pair.OutboundChannel, pair.OutboundQueue, 5000, correlationId)
                    .ShouldNotBeNull("Message not received at the destination queue within the 5-second timeout.")
                    .ShouldBe(messageText);
            }
        }
    }
}
