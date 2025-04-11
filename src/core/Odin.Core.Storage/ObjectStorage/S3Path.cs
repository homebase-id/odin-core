using System.Linq;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

public static class S3Path
{
    // Combine combines multiple path segments into a single path in an S3 compatible format.
    // See unit test for examples
    public static string Combine(params string[] paths)
    {
        if (paths.Length == 0)
        {
            return string.Empty;
        }

        var hasTrailingSlash = paths[^1].Replace('\\', '/').EndsWith('/');

        // The grokster was here:
        var normalizedPaths = paths
            .Select(p => (p ?? string.Empty).Replace('\\', '/').TrimStart('/').TrimEnd('/'))
            .Where(p => !string.IsNullOrEmpty(p))
            .ToArray();

        if (normalizedPaths.Length == 0)
        {
            return string.Empty;
        }

        if (hasTrailingSlash)
        {
            normalizedPaths[^1] += '/';
        }

        return string.Join("/", normalizedPaths);
    }

    //

    public static void AssertFileName(string fileName)
    {
        fileName = fileName.Trim();
        if (fileName.Length == 0 || fileName[^1] == '/' || fileName[^1] == '\\')
        {
            throw new S3StorageException($"Invalid file name {fileName}");
        }
    }

    //

    public static void AssertFolderName(string folderName)
    {
        folderName = folderName.Trim();
        if (folderName.Length == 0 || (folderName[^1] != '/' && folderName[^1] != '\\'))
        {
            throw new S3StorageException($"Invalid folder name {folderName} (must end with a slash)");
        }
    }

}
