using System;
using System.IO;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Images;
using System.Linq;
using System.Collections.Generic;

namespace Tix.IBMMQ.Bridge.E2ETests.Helpers
{
    public class BridgeContainer
    {
        private readonly IContainer _container;
        private readonly IFutureDockerImage _image;
        private readonly string _imageName;

        public BridgeContainer()
        {
            _imageName = $"ibmmq-bridge-e2e_{Guid.NewGuid()}";
            _image = new ImageFromDockerfileBuilder()
                .WithName(_imageName)
                .WithDockerfileDirectory(GetBridgeSolutionProjectDir())
                .Build();

            var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            _container = new ContainerBuilder()
                .WithImage(_image)
                .WithBindMount(appSettingsPath, "/app/appsettings.json")
                .Build();
        }

        private static string GetBridgeSolutionProjectDir()
        {
            var bridgeSolutionProjectDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "Tix.IBMMQ.Bridge"));
            var directoryInfo = new DirectoryInfo(bridgeSolutionProjectDir);
            if (!directoryInfo.Exists)
                throw new DirectoryNotFoundException($"The directory {bridgeSolutionProjectDir} does not exist.");
            if (directoryInfo.GetFiles("*.sln").Length != 1)
                throw new FileNotFoundException($"A solution file is required in {bridgeSolutionProjectDir}. It's required for building the Docker image.");
            if (!directoryInfo.GetFiles("Dockerfile").Any())
                throw new FileNotFoundException($"No Dockerfile found in {bridgeSolutionProjectDir}. It's required for building the Docker image.");
            return bridgeSolutionProjectDir;
        }

        public async Task StartAsync()
        {
            await _image.CreateAsync(); // Assicura che l'immagine sia creata prima di avviare il container
            await _container.StartAsync();
        }

        public async Task StopAsync()
        {
            await _container.StopAsync();
        }

        public async Task DisposeAsync()
        {
            await _container.DisposeAsync();

            using var client = new DockerClientConfiguration(new Uri("npipe://./pipe/docker_engine")).CreateClient();

            try
            {
                await client.Images.DeleteImageAsync(_imageName, new ImageDeleteParameters { Force = true });
            }
            catch (DockerImageNotFoundException)
            {
                // L'immagine potrebbe già essere stata rimossa, ignora l'eccezione
            }

            await RemoveDanglingImagesAsync(client);
        }

        private async Task RemoveDanglingImagesAsync(DockerClient client)
        {
            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters { All = true });
            var imagesInUse = new HashSet<string>(containers.Select(c => c.ImageID));

            var danglingImages = await client.Images.ListImagesAsync(new ImagesListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    { "dangling", new Dictionary<string, bool> { { "true", true } } }
                }
            });


            foreach (var image in danglingImages.Where(x => !imagesInUse.Contains(x.ID)))
            {
                try
                {
                    await client.Images.DeleteImageAsync(image.ID, new ImageDeleteParameters { Force = true });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to delete image {image.ID}: {ex.Message}");
                }
            }
        }
    }
}
