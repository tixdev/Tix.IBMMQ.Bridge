using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using Docker.DotNet;
using System.IO;
using ICSharpCode.SharpZipLib.Tar;
using System.Threading;

namespace Tix.IBMMQ.Bridge.E2ETests.Helpers;

public class DockerHelper
{
    private readonly DockerClient _client;

    public DockerHelper()
    {
        var dockerUri = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");

        _client = new DockerClientConfiguration(dockerUri).CreateClient();
    }

    public async Task<bool> ImageExists(string imageName)
    {
        var filters = new Dictionary<string, IDictionary<string, bool>>
        {
            ["reference"] = new Dictionary<string, bool> { [imageName] = true }
        };

        var images = await _client.Images.ListImagesAsync(new ImagesListParameters { Filters = filters });
        return images.Count > 0;
    }

    public async Task BuildImage(string contextFolder, string imageName, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(contextFolder))
            throw new DirectoryNotFoundException($"La cartella {contextFolder} non esiste.");

        using var tarStream = CreateTarballFromDirectory(contextFolder);

        var parameters = new ImageBuildParameters
        {
            Dockerfile = "Dockerfile",
            Tags = new[] { imageName },
            Remove = true,
        };

        var progress = new Progress<JSONMessage>(message =>
        {
            if (!string.IsNullOrEmpty(message.Stream))
                Console.Write(message.Stream);
            else if (!string.IsNullOrEmpty(message.Status))
                Console.WriteLine($"{message.Status} {message.ProgressMessage}");
            else if (!string.IsNullOrEmpty(message.ErrorMessage))
                Console.Error.WriteLine($"Errore: {message.ErrorMessage}");
        });

        await _client.Images.BuildImageFromDockerfileAsync(
            parameters,
            tarStream,
            authConfigs: null,
            headers: null,
            progress: progress,
            cancellationToken: cancellationToken);

        Console.WriteLine("Build completata!");
    }

    private static Stream CreateTarballFromDirectory(string sourceDirectory)
    {
        var outputStream = new MemoryStream();

        using (var tarOutputStream = new TarOutputStream(outputStream, System.Text.Encoding.UTF8))
        {
            tarOutputStream.IsStreamOwner = false;
            AddDirectoryFilesToTar(tarOutputStream, sourceDirectory, string.Empty);
            tarOutputStream.Close();
        }

        outputStream.Seek(0, SeekOrigin.Begin);
        return outputStream;
    }

    private static void AddDirectoryFilesToTar(TarOutputStream tarOutputStream, string sourceDirectory, string currentFolder)
    {
        string folder = Path.Combine(sourceDirectory, currentFolder);

        foreach (var filename in Directory.GetFiles(folder))
        {
            string tarName = Path.Combine(currentFolder, Path.GetFileName(filename)).Replace("\\", "/");

            using (var fileStream = File.OpenRead(filename))
            {
                var tarEntry = TarEntry.CreateTarEntry(tarName);
                tarEntry.Size = fileStream.Length;
                tarOutputStream.PutNextEntry(tarEntry);
                fileStream.CopyTo(tarOutputStream);
                tarOutputStream.CloseEntry();
            }
        }

        foreach (var directory in Directory.GetDirectories(folder))
        {
            string dirName = Path.Combine(currentFolder, Path.GetFileName(directory)).Replace("\\", "/");
            AddDirectoryFilesToTar(tarOutputStream, sourceDirectory, dirName);
        }
    }

    public async Task RemoveDanglingImages()
    {
        var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters { All = true });
        var imagesInUse = new HashSet<string>(containers.Select(c => c.ImageID));

        var danglingImages = await _client.Images.ListImagesAsync(new ImagesListParameters
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
                await _client.Images.DeleteImageAsync(image.ID, new ImageDeleteParameters { Force = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to delete image {image.ID}: {ex.Message}");
            }
        }
    }

    public async Task RemoveAllImageTags(string imageNameWithoutTag) =>
        await RemoveImages(imageNameWithoutTag, true);

    public async Task RemoveImage(string imageNameWithTag) =>
        await RemoveImages(imageNameWithTag, false);

    private async Task RemoveImages(string imageName, bool removeAllTags, bool force = true)
    {
        // Lista tutte le immagini (inclusi i tag)
        var images = await _client.Images.ListImagesAsync(new ImagesListParameters { All = true });

        var searchName = removeAllTags ? $"{imageName}:" : imageName;

        // Filtra le immagini che hanno almeno un repo tag che inizia con imageName + ":"
        var imagesToDelete = images
            .Where(img => img.RepoTags != null && img.RepoTags.Any(tag =>
                tag.StartsWith(searchName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var image in imagesToDelete)
        {
            try
            {
                Console.WriteLine($"Rimuovo immagine: ID={image.ID}, Tags=[{string.Join(", ", image.RepoTags)}]");
                await _client.Images.DeleteImageAsync(image.ID, new ImageDeleteParameters { Force = force });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore nella rimozione dell'immagine {image.ID}: {ex.Message}");
            }
        }
    }

}
