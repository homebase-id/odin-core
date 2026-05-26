using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public class InboxFileReaderWriter(FileReaderWriter fileReaderWriter) : IInboxReaderWriter
{
    public Task<uint> WriteStreamAsync(string filePath, Stream stream, CancellationToken cancellationToken = default)
        => fileReaderWriter.WriteStreamAsync(filePath, stream);

    public Task<byte[]> GetFileBytesAsync(string filePath, CancellationToken cancellationToken = default)
        => fileReaderWriter.GetAllFileBytesAsync(filePath);

    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
        => Task.FromResult(fileReaderWriter.FileExists(filePath));

    public Task EnsureDirectoryAsync(string dir, CancellationToken cancellationToken = default)
    {
        fileReaderWriter.CreateDirectory(dir);
        return Task.CompletedTask;
    }

    public Task DeleteByPrefixAsync(string pathPrefix, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(pathPrefix);
        var filePrefix = Path.GetFileName(pathPrefix);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            var matches = Directory.GetFiles(dir, filePrefix + "*");
            fileReaderWriter.DeleteFiles(matches);
        }
        return Task.CompletedTask;
    }

    public Task PromoteToAsync(string inboxRelativePath, string destResolvedKey, CancellationToken cancellationToken = default)
    {
        fileReaderWriter.CopyPayloadFile(inboxRelativePath, destResolvedKey);
        return Task.CompletedTask;
    }
}
