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
    /// Temporary storage for a given drive. Used to stage incoming file parts from uploads.
    /// </summary>
    public class UploadStorageManager(
        FileHandlerShared shared,
        IDriveManager driveManager,
        ILogger<UploadStorageManager> logger,
        TenantContext tenantContext)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        public async Task<bool> UploadFileExists(InternalDriveFileId file, string extension)
        {
            string path = await GetFilenameAndPathInternal(file, extension);
            return shared.FileExists(path);
        }

        /// <summary>
        /// Gets all bytes for the specified file
        /// </summary>
        public async Task<byte[]> GetAllFileBytes(InternalDriveFileId file, string extension)
        {
            string path = await GetFilenameAndPathInternal(file, extension);
            return await shared.GetAllFileBytesAsync(path);
        }

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        public async Task<uint> WriteStream(InternalDriveFileId file, string extension, Stream stream)
        {
            string path = await GetFilenameAndPathInternal(file, extension, true);
            return await shared.WriteStreamAsync(path, stream);
        }

        /// <summary>
        /// Deletes all files matching <param name="file"></param> regardless of extension
        /// </summary>
        public async Task CleanupUploadFiles(InternalDriveFileId file, List<PayloadDescriptor> descriptors)
        {
            await CleanupFilesInternal(file, descriptors);
        }

        /// <summary>
        /// Gets the physical path of the specified file
        /// </summary>
        public async Task<string> GetPath(InternalDriveFileId file, string extension)
        {
            return await GetFilenameAndPathInternal(file, extension);
        }

        private async Task CleanupFilesInternal(InternalDriveFileId file, List<PayloadDescriptor> descriptors)
        {
            try
            {
                if (!descriptors?.Any() ?? false)
                {
                    return;
                }

                var drive = await driveManager.GetDriveAsync(file.DriveId);

                var targetFiles = new List<string>();

                descriptors!.ForEach(descriptor =>
                {
                    var payloadExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(descriptor.Key, descriptor.Uid);
                    string payloadDirectoryAndFilename = GetFilenameAndPathInternal(drive, file, payloadExtension);
                    targetFiles.Add(payloadDirectoryAndFilename);

                    descriptor.Thumbnails?.ForEach(thumb =>
                    {
                        var thumbnailExtension = TenantPathManager.GetThumbnailFileNameAndExtension(descriptor.Key,
                            descriptor.Uid,
                            thumb.PixelWidth,
                            thumb.PixelHeight);
                        string thumbnailDirectoryAndFilename = GetFilenameAndPathInternal(drive, file, thumbnailExtension);
                        targetFiles.Add(thumbnailDirectoryAndFilename);
                    });
                });

                shared.DeleteFiles(targetFiles);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failure while cleaning up upload files");
            }
        }

        private async Task<string> GetFilenameAndPathInternal(InternalDriveFileId file, string extension, bool ensureExists = false)
        {
            var drive = await driveManager.GetDriveAsync(file.DriveId);
            return GetFilenameAndPathInternal(drive, file, extension, ensureExists);
        }

        private string GetFilenameAndPathInternal(StorageDrive drive, InternalDriveFileId file, string extension, bool ensureExists = false)
        {
            string dir = drive.GetDriveUploadPath();

            if (ensureExists)
            {
                shared.EnsureDirectoryExists(dir);
            }

            return shared.BuildStagingFilePath(dir, file.FileId, extension);
        }
    }
}
