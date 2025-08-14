using System.IO;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Tix.IBMMQ.Bridge.E2ETests.Helpers
{
    public class BridgeContainer
    {
        private readonly IContainer _container;

        public BridgeContainer()
        {
            var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

            _container = new ContainerBuilder()
                .WithImage("tix-ibmmq-bridge:latest") // Assuming a local image is built
                .WithName($"mq-bridge-e2e-{Path.GetRandomFileName()}")
                .WithMount(appSettingsPath, "/app/appsettings.json")
                .Build();
        }

        public async Task StartAsync()
        {
            await _container.StartAsync();
        }

        public async Task StopAsync()
        {
            await _container.StopAsync();
        }

        public async Task DisposeAsync()
        {
            await _container.DisposeAsync();
        }
    }
}
