using Microsoft.Extensions.Configuration;
using Shouldly;
using Tix.IBMMQ.Bridge.Options;
using Xunit;

namespace Tix.IBMMQ.Bridge.Tests.Options;

public class ConfigurationBindingTests
{
    [Fact]
    public void Should_bind_MQBridge_options()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var opts = config.GetSection("MQBridge").Get<MQBridgeOptions>();

        opts.ShouldNotBeNull();
        opts!.Connections.ShouldContainKey("ConnA");
        opts.QueuePairs.Count.ShouldBeGreaterThan(0);
        opts.Connections["ConnA"].QueueManagerName.ShouldBe("QM1");
        opts.QueuePairs[0].InboundChannel.ShouldBe("SVRCONN.CHANNEL");
    }
}
