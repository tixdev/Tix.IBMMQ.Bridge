using System;
using System.IO;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Docker.DotNet;
using Docker.DotNet.Models;
using System.Linq;
using System.Collections.Generic;

namespace Tix.IBMMQ.Bridge.E2ETests.Helpers
{
    public class MqBridgeContainer
    {
        private readonly IContainer _container;
        private readonly string _imageNamePrefix = "ibmmq-bridge-e2e";
        private readonly string _imageName;
        private readonly string _imageFolder;
        private readonly DockerHelper _docker = new DockerHelper();

        public MqBridgeContainer(string appSettingsPath)
        {            
            _imageFolder = GetBridgeSolutionProjectDir();
            _imageName = _imageNamePrefix + ":";
            _imageName += DirectoryHash.Compute(_imageFolder).Substring(0, 12).ToLowerInvariant();

            _container = new ContainerBuilder()
                .WithImage(_imageName)
                .WithBindMount(appSettingsPath, "/app/appsettings.json")
                .Build();
        }

        public async Task StartAsync()
        {
            if (!await _docker.ImageExists(_imageName))
            {
                await _docker.RemoveAllImageTags(_imageNamePrefix);
                await _docker.BuildImage(_imageFolder, _imageName);
            }

            await _container.StartAsync();
        }

        public async Task StopAsync()
        {
            await _container.StopAsync();
        }

        public async Task DisposeAsync()
        {
            await _container.DisposeAsync();
            await _docker.RemoveDanglingImages();
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
    }
}
