using System;
using System.IO;
using System.Text.RegularExpressions;
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
    private static readonly Regex SafeChars = new (@"^[a-zA-Z0-9\-_./]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

    //

    public async Task WriteFileAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default)
    {
        try
        {
            var relativePath = GetRelativeS3Path(filePath);
            await s3PayloadsStorage.WriteAllBytesAsync(relativePath, bytes, cancellationToken);
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
            var relativePath = GetRelativeS3Path(filePath);
            await s3PayloadsStorage.DeleteFileAsync(relativePath, cancellationToken);
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
            var relativePath = GetRelativeS3Path(filePath);
            return await s3PayloadsStorage.FileExistsAsync(relativePath, cancellationToken);
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
            var srcRelativePath = GetRelativeS3Path(srcFilePath);
            var dstRelativePath = GetRelativeS3Path(dstFilePath);
            await s3PayloadsStorage.MoveFileAsync(srcRelativePath, dstRelativePath, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public async Task<string[]> GetFilesInDirectoryAsync(
        string dir,
        string searchPattern = "*",
        CancellationToken cancellationToken = default)
    {
        var relativePath = GetRelativeS3Path(dir);
        var files = await s3PayloadsStorage.ListFilesAsync(relativePath, false, cancellationToken);
        return files.ToArray();
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

    // Maps _tenantPathManager.GetPayloadDirectoryAndFileName to a relative S3 path
    // e.g. "/data/tenants/payloads/<tenant-id>/drives" becomes "<tenant-id>/drives"
    public string GetRelativeS3Path(string absoluteFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(absoluteFilePath, nameof(absoluteFilePath));

        var root = _tenantPathManager.RootPayloadsPath;
        if (!absoluteFilePath.StartsWith(_tenantPathManager.RootPayloadsPath))
        {
            throw new ArgumentException($"The path '{absoluteFilePath}' does not start with the expected root path.",
                nameof(absoluteFilePath));
        }

        var relativePath = absoluteFilePath[root.Length..].Replace('\\', '/');

        // Sanity #1
        if (!SafeChars.IsMatch(relativePath))
        {
            throw new ArgumentException("File path contains invalid characters.", nameof(relativePath));
        }

        // Sanity #2
        if (relativePath.StartsWith('/'))
        {
            relativePath = relativePath[1..];
        }

        return relativePath;
    }

    //

}
