using System.IO;

namespace Odin.Core.Util;

public static class DirectoryInfoExtensions
{
    // Lifted from https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-copy-directories
    public static void CopyTo(this DirectoryInfo source, string target)
    {
        // Check if the source directory exists
        if (!source.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {source.FullName}");
        }

        // Cache directories before we start copying
        var dirs = source.GetDirectories();

        // Create the destination directory
        Directory.CreateDirectory(target);

        // Get the files in the source directory and copy to the destination directory
        foreach (var file in source.GetFiles())
        {
            var targetFilePath = Path.Combine(target, file.Name);
            file.CopyTo(targetFilePath);
        }

        foreach (var dir in dirs)
        {
            var newDestinationDir = Path.Combine(target, dir.Name);
            var dirInfo = new DirectoryInfo(dir.FullName);
            dirInfo.CopyTo(newDestinationDir);
        }
    }
}