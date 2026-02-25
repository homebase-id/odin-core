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
    /// Storage manager for upload and inbox files.
    /// </summary>
    public class FileStorageManager(
        FileReaderWriter fileReaderWriter,
        IDriveManager driveManager,
        ILogger<FileStorageManager> logger,
        TenantContext tenantContext)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        public async Task<bool> FileExists(UploadFile file, string extension)
        {
            string path = await GetUploadPathInternal(file, extension);
            return fileReaderWriter.FileExists(path);
        }

        public async Task<bool> FileExists(InboxFile file, string extension)
        {
            string path = await GetInboxPathInternal(file, extension);
            return fileReaderWriter.FileExists(path);
        }

        public async Task<byte[]> GetAllFileBytes(UploadFile file, string extension)
        {
            string path = await GetUploadPathInternal(file, extension);
            logger.LogDebug("Getting upload file bytes for [{path}]", path);
            var bytes = await fileReaderWriter.GetAllFileBytesAsync(path);
            logger.LogDebug("Got {count} bytes from {path}", bytes.Length, path);
            return bytes;
        }

        public async Task<byte[]> GetAllFileBytes(InboxFile file, string extension)
        {
            string path = await GetInboxPathInternal(file, extension);
            logger.LogDebug("Getting inbox file bytes for [{path}]", path);
            var bytes = await fileReaderWriter.GetAllFileBytesAsync(path);
            logger.LogDebug("Got {count} bytes from {path}", bytes.Length, path);
            return bytes;
        }

        public async Task<uint> WriteStream(UploadFile file, string extension, Stream stream)
        {
            string path = await GetUploadPathInternal(file, extension, true);
            return await WriteStreamInternal(path, stream);
        }

        public async Task<uint> WriteStream(InboxFile file, string extension, Stream stream)
        {
            string path = await GetInboxPathInternal(file, extension, true);
            return await WriteStreamInternal(path, stream);
        }

        private async Task<uint> WriteStreamInternal(string path, Stream stream)
        {
            logger.LogDebug("Writing file: {filePath}", path);
            var bytesWritten = await fileReaderWriter.WriteStreamAsync(path, stream);
            if (bytesWritten == 0)
            {
                logger.LogDebug("I didn't write anything to {filePath}", path);
            }
            else if (!File.Exists(path))
            {
                logger.LogError("I wrote {count} bytes, but file is not there {filePath}", bytesWritten, path);
            }

            logger.LogDebug("Wrote {count} bytes to {filePath}", bytesWritten, path);
            return bytesWritten;
        }

        public async Task CleanupInboxFiles(InboxFile file, List<PayloadDescriptor> descriptors)
        {
            logger.LogDebug("CleanupInboxFiles called - file: {file}", file);
            await CleanupInboxFilesInternal(file, descriptors);
            
            string[] additionalFiles =
            [
                await GetInboxPathInternal(file, TenantPathManager.MetadataExtension),
                await GetInboxPathInternal(file, TenantPathManager.TransferInstructionSetExtension)
            ];

            foreach (var filePath in additionalFiles)
            {
                logger.LogDebug("CleanupInboxFiles Deleting additional File: {file}", filePath);
            }
            
            fileReaderWriter.DeleteFiles(additionalFiles);
        }

        public async Task CleanupUploadedFiles(UploadFile file, List<PayloadDescriptor> descriptors)
        {
            logger.LogDebug("CleanupUploadedFiles called - file: {file}", file);
            await CleanupUploadFilesInternal(file, descriptors);
        }

        private async Task CleanupInboxFilesInternal(InboxFile file, List<PayloadDescriptor> descriptors)
        {
            try
            {
                if (!descriptors?.Any() ?? false)
                {
                    return;
                }

                var drive = await driveManager.GetDriveAsync(file.FileId.DriveId);
                var targetFiles = new List<string>();

                descriptors!.ForEach(descriptor =>
                {
                    var payloadExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(descriptor.Key, descriptor.Uid);
                    string payloadDirectoryAndFilename = GetInboxPathFromDrive(drive, file.FileId, payloadExtension);
                    targetFiles.Add(payloadDirectoryAndFilename);

                    descriptor.Thumbnails?.ForEach(thumb =>
                    {
                        var thumbnailExtension = TenantPathManager.GetThumbnailFileNameAndExtension(descriptor.Key,
                            descriptor.Uid,
                            thumb.PixelWidth,
                            thumb.PixelHeight);
                        string thumbnailDirectoryAndFilename = GetInboxPathFromDrive(drive, file.FileId, thumbnailExtension);
                        targetFiles.Add(thumbnailDirectoryAndFilename);
                    });
                });

                fileReaderWriter.DeleteFiles(targetFiles);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failure while cleaning up inbox files");
            }
        }

        private async Task CleanupUploadFilesInternal(UploadFile file, List<PayloadDescriptor> descriptors)
        {
            try
            {
                if (!descriptors?.Any() ?? false)
                {
                    return;
                }

                var drive = await driveManager.GetDriveAsync(file.FileId.DriveId);
                var targetFiles = new List<string>();

                descriptors!.ForEach(descriptor =>
                {
                    var payloadExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(descriptor.Key, descriptor.Uid);
                    string payloadDirectoryAndFilename = GetUploadPathFromDrive(drive, file.FileId, payloadExtension);
                    targetFiles.Add(payloadDirectoryAndFilename);

                    descriptor.Thumbnails?.ForEach(thumb =>
                    {
                        var thumbnailExtension = TenantPathManager.GetThumbnailFileNameAndExtension(descriptor.Key,
                            descriptor.Uid,
                            thumb.PixelWidth,
                            thumb.PixelHeight);
                        string thumbnailDirectoryAndFilename = GetUploadPathFromDrive(drive, file.FileId, thumbnailExtension);
                        targetFiles.Add(thumbnailDirectoryAndFilename);
                    });
                });

                fileReaderWriter.DeleteFiles(targetFiles);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failure while cleaning up upload files");
            }
        }

        public async Task<string> GetPath(UploadFile file, string extension)
        {
            return await GetUploadPathInternal(file, extension);
        }

        public async Task<string> GetPath(InboxFile file, string extension)
        {
            return await GetInboxPathInternal(file, extension);
        }

        private async Task<string> GetUploadPathInternal(UploadFile file, string extension, bool ensureExists = false)
        {
            var drive = await driveManager.GetDriveAsync(file.FileId.DriveId);
            return GetUploadPathFromDrive(drive, file.FileId, extension, ensureExists);
        }

        private async Task<string> GetInboxPathInternal(InboxFile file, string extension, bool ensureExists = false)
        {
            var drive = await driveManager.GetDriveAsync(file.FileId.DriveId);
            return GetInboxPathFromDrive(drive, file.FileId, extension, ensureExists);
        }

        private string GetUploadPathFromDrive(StorageDrive drive, InternalDriveFileId fileId, string extension, bool ensureExists = false)
        {
            string path = drive.GetDriveUploadPath();
            if (ensureExists)
            {
                Directory.CreateDirectory(path);
            }
            return Path.Combine(path, TenantPathManager.GetFilename(fileId.FileId, extension));
        }

        private string GetInboxPathFromDrive(StorageDrive drive, InternalDriveFileId fileId, string extension, bool ensureExists = false)
        {
            string path = drive.GetDriveInboxStoragePath();
            if (ensureExists)
            {
                Directory.CreateDirectory(path);
            }
            return Path.Combine(path, TenantPathManager.GetFilename(fileId.FileId, extension));
        }
    }
}
