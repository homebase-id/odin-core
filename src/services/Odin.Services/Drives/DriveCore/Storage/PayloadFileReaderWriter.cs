using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public class PayloadFileReaderWriter(
    ILogger<PayloadFileReaderWriter> logger,
    TenantContext tenantContext,
    FileReaderWriter fileReaderWriter
) : IPayloadReaderWriter
{
    private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

    //

    public async Task WriteFileAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(filePath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await fileReaderWriter.WriteAllBytesAsync(filePath, bytes, cancellationToken);
    }

    //

    public Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        fileReaderWriter.DeleteFile(filePath);
        return Task.CompletedTask;
    }
    
    //

    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(fileReaderWriter.FileExists(filePath));
    }

    //

    public Task MoveFileAsync(string srcFilePath, string dstFilePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(srcFilePath))
        {
            throw new FileNotFoundException(srcFilePath);
        }

        var dstDirectory = Path.GetDirectoryName(dstFilePath);
        if (dstDirectory != null && !Directory.Exists(dstDirectory))
        {
            Directory.CreateDirectory(dstDirectory);
        }

        fileReaderWriter.MoveFile(srcFilePath, dstFilePath);
        return Task.CompletedTask;
    }

    //

    public void MoveFileXYZ(string sourceFilePath, string destinationFilePath)
    {
        throw new System.NotImplementedException();
    }

    public string[] GetFilesInDirectoryXYZ(string dir, string searchPattern = "*")
    {
        throw new System.NotImplementedException();
    }

    public void DeleteFilesInDirectoryXYZ(string dir, string searchPattern)
    {
        throw new System.NotImplementedException();
    }

    public bool DirectoryExistsXYZ(string dir)
    {
        throw new System.NotImplementedException();
    }

    public Stream OpenStreamForReadingXYZ(string filePath)
    {
        throw new System.NotImplementedException();
    }

    public void CopyPayloadFileXYZ(string sourcePath, string targetPath)
    {
        throw new System.NotImplementedException();
    }

    public void CreateDirectoryXYZ(string dir)
    {
        throw new System.NotImplementedException();
    }
}