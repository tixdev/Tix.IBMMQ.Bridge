using System.Threading.Tasks;
using Tix.IBMMQ.Bridge.Options;
using Xunit;
using Xunit.Abstractions;
using Tix.IBMMQ.Bridge.IntegrationTests.Helpers;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class ReliabilityTestsFixture : IAsyncLifetime
{
    public MqContainer MqIn { get; set; }
    public MqContainer MqOut { get; set; }
    private MqBridgeHost MqBridge { get; set; }

    public ReliabilityTestsFixture()
    {
        // Configurazione delle immagini per i contenitori
        var imageIn = new ContainerImage(/*old ver*/);
        var imageOut = new ContainerImage();

        MqIn = imageIn.BuildMqContainer("reliability-test-in.mqsc");
        MqOut = imageOut.BuildMqContainer("reliability-test-out.mqsc");
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Avvia i server MQ
            await MqIn.InitializeAsync();
            await MqOut.InitializeAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public void InitBridge(ITestOutputHelper logger, params (string Channel, string QueueName)[] pairs)
    {
        if (MqBridge != null)
            return; // Bridge already initialized

        MqBridge = new MqBridgeHost(logger);
        MqBridge.SetConnections(MqIn.Connection, MqOut.Connection);
        foreach(var p in pairs)
            MqBridge.AddQueuePair(p.Channel, p.QueueName);
    }

    public async Task RestartBridge() => await MqBridge.RestartAsync();

    public async Task StopBridge() => await MqBridge.StopAsync();

    public async Task DisposeAsync()
    {
        // Ferma il MQ Bridge e libera le risorse
        if (MqBridge != null)
            await MqBridge.StopAsync();

        await MqIn.DisposeAsync();
        await MqOut.DisposeAsync();
    }
}
