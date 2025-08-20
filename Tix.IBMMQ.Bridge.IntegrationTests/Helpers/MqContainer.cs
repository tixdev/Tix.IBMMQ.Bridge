using System.IO;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Tix.IBMMQ.Bridge.Options;
using Xunit;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Helpers
{
    public class MqContainer(ContainerImage image): IAsyncLifetime
    {
        const string MqAppUser = "app";
        const string MqAdminUser = "admin";
        const string Password = "passw0rd";
        const string MqManagerName = "QM1";
        const int MqPort = 1414;

        private IContainer _container;

        public MqContainer Build(string mqStartupScriptPath, bool exposeWebConsole)
        {
            var builder = new ContainerBuilder()
                .WithImage(image.Name)
                .WithEnvironment("LICENSE", "accept")
                .WithEnvironment("MQ_QMGR_NAME", MqManagerName)
                .WithEnvironment($"MQ_{MqAppUser}_PASSWORD", Password)
                .WithExposedPort(MqPort)
                .WithPortBinding(MqPort, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1414));

            if (exposeWebConsole)
                builder = builder
                    .WithEnvironment($"MQ_{MqAdminUser}_PASSWORD", Password)
                    .WithExposedPort(9443)
                    .WithPortBinding(9443, true);

            if (mqStartupScriptPath != null)
                builder = File.Exists(mqStartupScriptPath) ?
                    builder.WithBindMount(mqStartupScriptPath, "/etc/mqm/startup-script.mqsc", AccessMode.ReadOnly) :
                    throw new FileNotFoundException(mqStartupScriptPath);
            
            _container = builder.Build();
            return this;
        }

        public async Task InitializeAsync() => await _container.StartAsync();

        public async Task DisposeAsync() => await _container.DisposeAsync();

        public ConnectionOptions GetMqConnectionOptions() => new ConnectionOptions
            {
                QueueManagerName = MqManagerName,
                ConnectionName = $"localhost({_container.GetMappedPublicPort(MqPort)})",
                UserId = MqAppUser,
                Password = Password,
                UseTls = false
            };
    }
}
