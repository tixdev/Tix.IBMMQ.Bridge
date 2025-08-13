using DotNet.Testcontainers.Configurations;
using Shouldly;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Tix.IBMMQ.Bridge.IntegrationTests.Helpers;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class ReliabilityTests : IClassFixture<ReliabilityTestsFixture>
{
    public const string Channel = "DEV.APP.SVRCONN";
    public const string QueueName = "RELIABILITY.TEST";

    private readonly ITestOutputHelper _logger;
    private readonly ReliabilityTestsFixture _fixture;

    static ReliabilityTests()
    {
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    public ReliabilityTests(ITestOutputHelper logger, ReliabilityTestsFixture fixture)
    {
        _logger = logger;
        _fixture = fixture;
        _fixture.InitBridge(_logger, Channel, QueueName);
    }

    private async Task InitTestAsync()
    {
        _fixture.ConnIn.DrainQueue(Channel, QueueName);
        _fixture.ConnOut.DrainQueue(Channel, QueueName);

        await _fixture.RestartBridge();
    }

    private int GetOutQueueDepth() => _fixture.ConnOut.GetQueueMaxDepth(Channel, QueueName);

    [Fact]
    public async Task Should_not_lose_messages_after_a_failure()
    {
        await InitTestAsync();

        int maxOutDepth = GetOutQueueDepth();
        int maxMsgLength = _fixture.ConnOut.GetQueueMaxMessageAvailableLength(Channel, QueueName);

        int msgToSend = Random.Shared.Next(0, maxOutDepth - 1);
        int msgStuck = maxOutDepth - msgToSend;

        for (var i = 0; i < maxOutDepth; i++)
        {
            int txtSize = maxMsgLength;
            // Force an error exceeding the message size
            txtSize += (msgToSend == i ? 1 : 0);
            _fixture.ConnIn.PutMessage(Channel, QueueName, new string('x', txtSize));
        }

        bool result = await TestHelper.Evaluate(_logger, () =>
        {
            _logger.WriteLine($"Expected {msgToSend} messages moved to the destination queue but {msgStuck} messages stuck in the source queue");
            int totIn = _fixture.ConnIn.GetQueueDepth(Channel, QueueName);
            int totOut = _fixture.ConnOut.GetQueueDepth(Channel, QueueName);
            _logger.WriteLine($"Destination queue {totOut}, Source queue {totIn}");
            return totIn == msgStuck && totOut == msgToSend;
        });

        result.ShouldBeTrue();

        await _fixture.StopBridge();
    }

    [Fact]
    public async Task Should_not_lose_messages_when_outbound_queue_is_full()
    {
        await InitTestAsync();

        const int extraMsg = 5;
        int maxOutDepth = GetOutQueueDepth();
        int totMsgToPut = maxOutDepth + extraMsg;

        _logger.WriteLine($"Putting {totMsgToPut} messages on {QueueName}");
        for (var i = 0; i < totMsgToPut; i++)
            _fixture.ConnIn.PutMessage(Channel, QueueName, $"Message {i}");

        bool result = await TestHelper.Evaluate(_logger, () =>
        {
            _logger.WriteLine($"Expected {maxOutDepth} messages moved to the destination queue but {extraMsg} messages stuck in the source queue");
            int totIn = _fixture.ConnIn.GetQueueDepth(Channel, QueueName);
            int totOut = _fixture.ConnOut.GetQueueDepth(Channel, QueueName);
            _logger.WriteLine($"Destination queue {totOut}, Source queue {totIn}");
            return totIn == extraMsg && totOut == maxOutDepth;
        });

        result.ShouldBeTrue();

        await _fixture.StopBridge();
    }

    //[Fact]
    //public async Task Should_not_lose_messages_when_outbound_queue_manager_is_unavailable()
    //{
    ////    TODO
    //}
}