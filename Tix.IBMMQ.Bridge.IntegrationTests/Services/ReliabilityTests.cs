using DotNet.Testcontainers.Configurations;
using Shouldly;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Tix.IBMMQ.Bridge.IntegrationTests.Helpers;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class ReliabilityTests : IClassFixture<ReliabilityTestsFixture>
{
    public const string Channel = "DEV.APP.SVRCONN";
    public const string TestQueue1 = "RELIABILITY.TEST";
    public const string PersistedQueue = "RELIABILITY.PERSIST";

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
        _fixture.InitBridge(_logger,
            (Channel, TestQueue1),
            (Channel, PersistedQueue));
    }

    private async Task InitTestAsync()
    {
        _fixture.MqIn.Connection.DrainQueue(Channel, TestQueue1);
        _fixture.MqIn.Connection.DrainQueue(Channel, PersistedQueue);
        _fixture.MqOut.Connection.DrainQueue(Channel, TestQueue1);
        _fixture.MqOut.Connection.DrainQueue(Channel, PersistedQueue);

        await _fixture.RestartBridge();
    }

    private int GetOutQueueDepth() => _fixture.MqOut.Connection.GetQueueMaxDepth(Channel, TestQueue1);

    [Fact]
    public async Task Should_not_lose_messages_after_a_failure()
    {
        await InitTestAsync();

        int maxOutDepth = GetOutQueueDepth();
        int maxMsgLength = _fixture.MqOut.Connection.GetQueueMaxMessageAvailableLength(Channel, TestQueue1);

        int msgToSend = Random.Shared.Next(0, maxOutDepth - 1);
        int msgStuck = maxOutDepth - msgToSend;

        for (var i = 0; i < maxOutDepth; i++)
        {
            int txtSize = maxMsgLength;
            // Force an error exceeding the message size
            txtSize += (msgToSend == i ? 1 : 0);
            _fixture.MqIn.Connection.PutMessage(Channel, TestQueue1, new string('x', txtSize));
        }

        bool result = await TestHelper.Evaluate(_logger, () =>
        {
            _logger.WriteLine($"Expected {msgToSend} messages moved to the destination queue but {msgStuck} messages stuck in the source queue");
            int totIn = _fixture.MqIn.Connection.GetQueueDepth(Channel, TestQueue1);
            int totOut = _fixture.MqOut.Connection.GetQueueDepth(Channel, TestQueue1);
            _logger.WriteLine($"Destination queue {totOut}, Source queue {totIn}");
            return totIn == msgStuck && totOut == msgToSend;
        });

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_not_lose_messages_when_outbound_queue_is_full()
    {
        await InitTestAsync();

        const int extraMsg = 5;
        int maxOutDepth = GetOutQueueDepth();
        int totMsgToPut = maxOutDepth + extraMsg;

        _logger.WriteLine($"Putting {totMsgToPut} messages on {TestQueue1}");
        for (var i = 0; i < totMsgToPut; i++)
            _fixture.MqIn.Connection.PutMessage(Channel, TestQueue1, $"Message {i}");

        bool result = await TestHelper.Evaluate(_logger, () =>
        {
            _logger.WriteLine($"Expected {maxOutDepth} messages moved to the destination queue but {extraMsg} messages stuck in the source queue");
            int totIn = _fixture.MqIn.Connection.GetQueueDepth(Channel, TestQueue1);
            int totOut = _fixture.MqOut.Connection.GetQueueDepth(Channel, TestQueue1);
            _logger.WriteLine($"Destination queue {totOut}, Source queue {totIn}");
            return totIn == extraMsg && totOut == maxOutDepth;
        });

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_not_lose_messages_on_outbound_outages()
    {
        await SendMessagesAndSimulateOutages(3000, _fixture.MqOut);
    }

    [Fact]
    public async Task Should_not_send_a_message_more_times_on_inbound_outages()
    {
        await SendMessagesAndSimulateOutages(3000, _fixture.MqIn);
    }

    private async Task SendMessagesAndSimulateOutages(int totMsg, MqContainer mq)
    {
        await InitTestAsync();

        var messages = Enumerable.Range(0, totMsg).Select(i => $"Message {i}");
        _fixture.MqIn.Connection.PutMessages(Channel, PersistedQueue, messages);

        // Simulate intermittent outbound server outages
        for (int i = 1; i <= 5; i++)
        {
            await Task.Delay(3000); // Let bridge work a while
            await mq.StopServerMq();
            await Task.Delay(1000);
            await mq.StartServerMq();
        }

        var result = await TestHelper.WaitUntilInboundIsEmpty(_logger,
            Channel, PersistedQueue,
            _fixture.MqIn.Connection,
            _fixture.MqOut.Connection);

        (result.TotOut == totMsg && result.TotIn == 0)
        .ShouldBeTrue($"Expected {totMsg} messages on outbound queue but received {result.TotOut}." + (result.TotIn > 0 ? $" Warning: {result.TotIn} still in the inbound queue after result timeout" : ""));
    }
}
