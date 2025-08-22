using System.Threading.Tasks;
using Tix.IBMMQ.Bridge.Options;
using Xunit;
using Xunit.Abstractions;
using Tix.IBMMQ.Bridge.IntegrationTests.Helpers;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class ReliabilityTestsFixture : IAsyncLifetime
{
    private MqContainer MqServerIn { get; set; }
    private MqContainer MqServerOut { get; set; }
    public ConnectionOptions ConnIn { get; private set; }
    public ConnectionOptions ConnOut { get; private set; }
    private MqBridgeHost MqBridge { get; set; }

    public ReliabilityTestsFixture()
    {
        // Configurazione delle immagini per i contenitori
        var imageIn = new ContainerImage(/*old ver*/);
        var imageOut = new ContainerImage();

        MqServerIn = imageIn.BuildMqContainer("reliability-test-in.mqsc");
        MqServerOut = imageOut.BuildMqContainer("reliability-test-out.mqsc");
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Avvia i server MQ
            await MqServerIn.InitializeAsync();
            await MqServerOut.InitializeAsync();

            // Ottieni le opzioni di connessione
            ConnIn = MqServerIn.GetMqConnectionOptions();
            ConnOut = MqServerOut.GetMqConnectionOptions();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public void InitBridge(ITestOutputHelper logger, string channel, string queueName)
    {
        if (MqBridge != null)
            return; // Bridge already initialized

        MqBridge = new MqBridgeHost(logger);
        MqBridge.SetConnections(ConnIn, ConnOut);
        MqBridge.AddQueuePair(channel, queueName);
    }

    public async Task RestartBridge() => await MqBridge.RestartAsync();

    public async Task StopBridge() => await MqBridge.StopAsync();

    public async Task DisposeAsync()
    {
        // Ferma il MQ Bridge e libera le risorse
        if (MqBridge != null)
            await MqBridge.StopAsync();

        await MqServerIn.DisposeAsync();
        await MqServerOut.DisposeAsync();
    }
}
