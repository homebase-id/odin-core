using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public enum TempStorageType
{
    Upload,
    Inbox
}

/// <summary>
/// Temporary storage for a given driven.  Used to stage incoming file parts from uploads and transfers.
/// </summary>
public abstract class AbstractTempStorageManager(
    ILogger logger,
    DriveFileReaderWriter driveFileReaderWriter,
    string subPath) // eg. "upload", "inbox"
{
    private const char PartsSeparator = '~';

    // public StorageDrive Drive { get; }

    /// <summary>
    /// Gets a stream of data for the specified file
    /// </summary>
    public async Task<byte[]> GetAllFileBytes(StorageDrive drive, Guid fileId, string extension)
    {
        string path = GetTempFilenameAndPath(drive, fileId, extension);
        logger.LogDebug("Getting temp file bytes for [{path}]", path);
        var bytes = await driveFileReaderWriter.GetAllFileBytesAsync(path);
        logger.LogDebug("Got {count} bytes from {path}", bytes?.Length, path);
        return bytes;
    }

    /// <summary>
    /// Writes a stream for a given file and part to the configured provider.
    /// </summary>
    public async Task<uint> WriteStream(StorageDrive drive, Guid fileId, string extension, Stream stream)
    {
        var filePath = GetTempFilenameAndPath(drive, fileId, extension);

        logger.LogDebug("Writing temp file: {filePath}", filePath);
        var bytesWritten = await driveFileReaderWriter.WriteStreamAsync(filePath, stream);
        if (bytesWritten == 0)
        {
            // Sanity #1
            logger.LogDebug("I didn't write anything to {filePath}", filePath);
        }
        else if (!File.Exists(filePath))
        {
            // Sanity #2
            logger.LogError("I wrote {count} bytes, but file is not there {filePath}", bytesWritten, filePath);
        }

        return bytesWritten;
    }

    /// <summary>
    /// Deletes all files matching <param name="fileId"></param> regardless of extension
    /// </summary>
    /// <param name="drive"></param>
    /// <param name="fileId"></param>
    // SEB:TODO delete this
    public Task EnsureDeleted(StorageDrive drive, Guid fileId)
    {
        var dir = "xxx";
        logger.LogDebug("no-op: delete on temp files called yet we've removed this. path {filePath}", dir);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the physical path of the specified file
    /// </summary>
    public string GetPath(StorageDrive drive, Guid fileId, string extension)
    {
        var filePath = GetTempFilenameAndPath(drive, fileId, extension);
        return filePath;
    }

    public void CleanUp(StorageDrive drive, Guid fileId)
    {
        var searchPath = drive.GetTempStoragePath();

        if (!Directory.Exists(searchPath))
        {
            return;
        }

        var fileMask = FileNameWithoutExtension(drive, fileId) + "*";

        var files = Directory.GetFiles(searchPath, fileMask);

        foreach (var file in files)
        {
            File.Delete(file);
        }
    }

    public string FileNameWithoutExtension(StorageDrive drive, Guid fileId)
    {
        // tenant-id (guid)                 drive id (guid)                  file id (guid)
        // 069bc32232514be5a9fdbfa7294002e2~0374b698e6794eadb25502e1b31c6e02~000c4819c0b0ea008c07feb329de6fd2.dflt_key-113126827408162816-139x300.thumb

        return
            drive.OwnerTenantId.ToString("N") +
            PartsSeparator +
            drive.Id.ToString("N") +
            PartsSeparator +
            DriveFileUtility.GetFileIdForStorage(fileId);
    }

    public string FilePathWithoutExtension(StorageDrive drive, Guid fileId)
    {
        return Path.Combine(drive.GetTempStoragePath(), subPath, FileNameWithoutExtension(drive, fileId));
    }

    public string DriveCompleteTempPath(StorageDrive drive)
    {
        return Path.Combine(drive.GetTempStoragePath(), subPath);
    }

    private string GetFilename(StorageDrive drive, Guid fileId, string extension)
    {
        var file = FileNameWithoutExtension(drive, fileId);
        return string.IsNullOrEmpty(extension) ? file : $"{file}.{extension.ToLower()}";
    }

    private string GetTempFilenameAndPath(StorageDrive drive, Guid fileId, string extension)
    {
        var dir = drive.GetTempStoragePath();
        return Path.Combine(dir, subPath, GetFilename(drive, fileId, extension));
    }
}

//

public class UploadTempStorageManager(
    ILogger<UploadTempStorageManager> logger,
    DriveFileReaderWriter driveFileReaderWriter)
    : AbstractTempStorageManager(logger, driveFileReaderWriter, UploadDir)
{
    public const string UploadDir = "upload";
}

//

public class InboxTempStorageManager(
    ILogger<InboxTempStorageManager> logger,
    DriveFileReaderWriter driveFileReaderWriter)
    : AbstractTempStorageManager(logger, driveFileReaderWriter, InboxDir)
{
    public const string InboxDir = "inbox";
}