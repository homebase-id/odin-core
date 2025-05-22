using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Storage.ObjectStorage;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public class PayloadS3ReaderWriter(
    TenantContext tenantContext,
    S3PayloadStorage s3PayloadsStorage) : IPayloadReaderWriter
{
    private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

    //

    public async Task WriteFileAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default)
    {
        try
        {
            await s3PayloadsStorage.WriteAllBytesAsync(filePath, bytes, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public async Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await s3PayloadsStorage.DeleteFileAsync(filePath, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }
    
    //

    public async Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            return await s3PayloadsStorage.FileExistsAsync(filePath, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public async Task MoveFileAsync(string srcFilePath, string dstFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await s3PayloadsStorage.MoveFileAsync(srcFilePath, dstFilePath, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public async Task<string[]> GetFilesInDirectoryAsync(string dir, CancellationToken cancellationToken = default)
    {
        try
        {
            var files = await s3PayloadsStorage.ListFilesAsync(dir, false, cancellationToken);
            return files.ToArray();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public Task CreateDirectoryAsync(string dir, CancellationToken cancellationToken = default)
    {
        // No-op: S3 does not have directories in the same way as a file system.
        return Task.CompletedTask;
    }

    //

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

    //

}
