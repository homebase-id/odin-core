using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.DriveCore.Storage
{
    /// <summary>
    /// Temporary storage for a given driven.  Used to stage incoming file parts from uploads and transfers.
    /// </summary>
    public class UploadStorageManager(
        DriveFileReaderWriter driveFileReaderWriter,
        DriveManager driveManager,
        ILogger<UploadStorageManager> logger,
        TenantContext tenantContext)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        public async Task<bool> TempFileExists(TempFile tempFile, string extension)
        {
            string path = await GetTempFilenameAndPathInternal(tempFile, extension);
            return driveFileReaderWriter.FileExists(path);
        }

        /// <summary>
        /// Gets a stream of data for the specified file
        /// </summary>
        public async Task<byte[]> GetAllFileBytes(TempFile tempFile, string extension)
        {
            string path = await GetTempFilenameAndPathInternal(tempFile, extension);

            logger.LogDebug("Getting temp file bytes for [{path}]", path);
            var bytes = await driveFileReaderWriter.GetAllFileBytesAsync(path);
            logger.LogDebug("Got {count} bytes from {path}", bytes.Length, path);
            return bytes;
        }

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        public async Task<uint> WriteStream(TempFile tempFile, string extension, Stream stream)
        {
            string path = await GetTempFilenameAndPathInternal(tempFile, extension, true);
            logger.LogDebug("Writing temp file: {filePath}", path);
            var bytesWritten = await driveFileReaderWriter.WriteStreamAsync(path, stream);
            if (bytesWritten == 0)
            {
                // Sanity #1
                logger.LogDebug("I didn't write anything to {filePath}", path);
            }
            else if (!File.Exists(path))
            {
                // Sanity #2
                logger.LogError("I wrote {count} bytes, but file is not there {filePath}", bytesWritten, path);
            }

            logger.LogDebug("Wrote {count} bytes to {filePath}", bytesWritten, path);

            return bytesWritten;
        }

        /// <summary>
        /// Deletes all files matching <param name="tempFile"></param> regardless of extension
        /// </summary>
        public async Task CleanupTempFiles(TempFile tempFile, List<PayloadDescriptor> descriptors)
        {
            try
            {
                if (!descriptors?.Any() ?? false)
                {
                    return;
                }

                var drive = await driveManager.GetDriveAsync(tempFile.File.DriveId);

                var dir = GetFileDirectory(drive, tempFile);
                var targetFiles = new List<string>();
                descriptors!.ForEach(descriptor =>
                {
                    var payloadFilename = TenantPathManager.CreateBasePayloadFileNameAndExtension(descriptor.Key, descriptor.Uid);
                    targetFiles.Add(Path.Combine(dir, payloadFilename));

                    descriptor.Thumbnails?.ForEach(thumb =>
                    {
                        var thumbnailFilename = TenantPathManager.CreateThumbnailFileNameAndExtension(descriptor.Key, descriptor.Uid,
                            thumb.PixelWidth, thumb.PixelHeight);
                        targetFiles.Add(Path.Combine(dir, thumbnailFilename));
                    });
                });

                driveFileReaderWriter.DeleteFiles(targetFiles);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failure while cleaning up temp files");
            }
        }

        /// <summary>
        /// Gets the physical path of the specified file
        /// </summary>
        public async Task<string> GetPath(TempFile tempFile, string extension)
        {
            string path = await GetTempFilenameAndPathInternal(tempFile, extension);
            return path;
        }

        private string GetFileDirectory(StorageDrive drive, TempFile tempFile, bool ensureExists = false)
        {
            var path = drive.GetTempStoragePath(tempFile.StorageType);

            if (ensureExists)
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }

        private async Task<string> GetTempFilenameAndPathInternal(TempFile tempFile, string extension, bool ensureExists = false)
        {
            var drive = await driveManager.GetDriveAsync(tempFile.File.DriveId);
            var fileId = tempFile.File.FileId;

            string dir = GetFileDirectory(drive, tempFile, ensureExists);
            var r = Path.Combine(dir, TenantPathManager.GetFilename(fileId, extension));

            return r;
        }
    }
}