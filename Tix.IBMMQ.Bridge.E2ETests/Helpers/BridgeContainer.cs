using System;
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

            // Navigates up from bin/Debug/net8.0 to the solution root
            var solutionDir = Directory.GetParent(Directory.GetCurrentDirectory())?.Parent?.Parent?.Parent?.FullName
                            ?? throw new DirectoryNotFoundException("Cannot find the solution directory.");

            _container = new ContainerBuilder()
                .WithImage(new ImageFromDockerfileBuilder()
                    .WithName($"tix-ibmmq-bridge-e2e:{Guid.NewGuid()}")
                    .WithDockerfileDirectory(solutionDir)
                    .WithDockerfile("Tix.IBMMQ.Bridge/Dockerfile") // Relative to DockerfileDirectory
                    .Build())
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
