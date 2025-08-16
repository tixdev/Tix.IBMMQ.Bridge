using System;
using System.IO;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Images;
using System.Linq;

namespace Tix.IBMMQ.Bridge.E2ETests.Helpers
{
    public class BridgeContainer
    {
        private readonly IContainer _container;
        private readonly IFutureDockerImage _image;
        private readonly string _imageName;

        public BridgeContainer()
        {
            _imageName = $"tix-ibmmq-bridge-e2e_{Guid.NewGuid()}";
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

            // // Trova e rimuovi la dangling image dello stage di build
            // try
            // {
            //     var images = await client.Images.ListImagesAsync(new ImagesListParameters { All = true });
            //     var mainImage = images.FirstOrDefault(img => img.RepoTags != null && img.RepoTags.Contains(_imageName));
            //     if (mainImage != null)
            //     {
            //         // Ottieni dettagli dell'immagine per accedere a Parent
            //         var mainImageDetails = await client.Images.InspectImageAsync(mainImage.ID);
            //         var parentId = mainImageDetails.Parent;
            //         if (!string.IsNullOrEmpty(parentId))
            //         {
            //             var buildStageImage = images.FirstOrDefault(img => img.ID == parentId && (img.RepoTags == null || img.RepoTags.All(tag => tag.StartsWith("<none>"))));
            //             if (buildStageImage != null)
            //             {
            //                 await client.Images.DeleteImageAsync(buildStageImage.ID, new ImageDeleteParameters { Force = true });
            //             }
            //         }
            //     }
            // }
            // catch
            // {
            //     // Ignora eventuali errori di rimozione
            // }

            try
            {
                await client.Images.DeleteImageAsync(_imageName, new ImageDeleteParameters { Force = true });
            }
            catch (DockerImageNotFoundException)
            {
                // L'immagine potrebbe già essere stata rimossa, ignora l'eccezione
            }
        }
    }
}
