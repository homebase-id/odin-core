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
    /// Temporary storage for a given drive. Used to stage incoming file parts from peer transfers (inbox).
    /// </summary>
    public class InboxStorageManager(
        FileHandlerShared shared,
        IDriveManager driveManager,
        ILogger<InboxStorageManager> logger,
        TenantContext tenantContext)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        public async Task<bool> InboxFileExists(InternalDriveFileId file, string extension)
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

        public async Task CleanupInboxFiles(InternalDriveFileId file, List<PayloadDescriptor> descriptors)
        {
            logger.LogDebug("CleanupInboxFiles called - file: {file}", file);

            await CleanupFilesInternal(file, descriptors);

            //TODO: the extensions should be centralized
            string[] additionalFiles =
            [
                await GetFilenameAndPathInternal(file, TenantPathManager.MetadataExtension),
                await GetFilenameAndPathInternal(file, TenantPathManager.TransferInstructionSetExtension)
            ];

            foreach (var f in additionalFiles)
            {
                logger.LogDebug("CleanupInboxFiles Deleting additional File: {file}", f);
            }

            // clean up the transfer header and metadata since we keep those in the inbox
            shared.DeleteFiles(additionalFiles);
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
                logger.LogError(e, "Failure while cleaning up inbox files");
            }
        }

        private async Task<string> GetFilenameAndPathInternal(InternalDriveFileId file, string extension, bool ensureExists = false)
        {
            var drive = await driveManager.GetDriveAsync(file.DriveId);
            return GetFilenameAndPathInternal(drive, file, extension, ensureExists);
        }

        private string GetFilenameAndPathInternal(StorageDrive drive, InternalDriveFileId file, string extension, bool ensureExists = false)
        {
            string dir = drive.GetDriveInboxPath();

            if (ensureExists)
            {
                shared.EnsureDirectoryExists(dir);
            }

            return shared.BuildTempFilePath(dir, file.FileId, extension);
        }
    }
}
