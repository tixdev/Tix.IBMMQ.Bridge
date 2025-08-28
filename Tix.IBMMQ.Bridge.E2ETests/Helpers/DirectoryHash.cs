using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Tix.IBMMQ.Bridge.E2ETests.Helpers
{
    static class DirectoryHash
    {
        private static readonly string[] AllowedExtensions = new[]
        {
            ".cs", ".sln", ".csproj", ".config", ".json", 
            ".xml", ".resx", ".props", ".targets"
        };

        private static readonly string[] ExcludedFolders = new[]
        {
            "bin", "obj",
            ".vs", ".git",
            ".github", "packages"
        };

        public static string Compute(string folderPath)
        {
            using var sha256 = SHA256.Create();

            var files = Directory
                .EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(file =>
                {
                    var ext = Path.GetExtension(file);
                    if (!AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        return false;

                    var relativePath = Path.GetRelativePath(folderPath, file);
                    // Controlla se uno dei segmenti del percorso è in ExcludedFolders
                    var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return !segments.Any(s => ExcludedFolders.Contains(s, StringComparer.OrdinalIgnoreCase));
                })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(folderPath, file);
                byte[] pathBytes = Encoding.UTF8.GetBytes(relativePath);
                sha256.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);

                byte[] contentBytes = File.ReadAllBytes(file);
                sha256.TransformBlock(contentBytes, 0, contentBytes.Length, contentBytes, 0);
            }

            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            return Convert.ToHexString(sha256.Hash);
        }
    }
}
