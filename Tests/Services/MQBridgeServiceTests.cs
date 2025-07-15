using Shouldly;
using Tix.IBMMQ.Bridge.Services;
using Xunit;

namespace Tix.IBMMQ.Bridge.Tests.Services;

public class MQBridgeServiceTests
{
    [Theory]
    [InlineData("host(1414)", "host", 1414)]
    [InlineData("example.com(1550)", "example.com", 1550)]
    public void Should_parse_connection_name(string connectionName, string expectedHost, int expectedPort)
    {
        var (host, port) = MQBridgeService.ParseConnectionName(connectionName);
        host.ShouldBe(expectedHost);
        port.ShouldBe(expectedPort);
    }
}
