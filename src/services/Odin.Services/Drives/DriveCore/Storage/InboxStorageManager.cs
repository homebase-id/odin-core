using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Storage
{
    /// <summary>
    /// Temporary storage for a given drive. Used to stage incoming file parts from peer transfers (inbox).
    /// </summary>
    public class InboxStorageManager(
        FileHandlerShared shared,
        ILogger<InboxStorageManager> logger,
        TenantContext tenantContext)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        public Task<bool> InboxFileExists(InternalDriveFileId file, string extension)
        {
            string path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return Task.FromResult(shared.FileExists(path));
        }

        /// <summary>
        /// Gets all bytes for the specified file
        /// </summary>
        public async Task<byte[]> GetAllInboxFileBytes(InternalDriveFileId file, string extension)
        {
            string path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return await shared.GetAllFileBytesAsync(path);
        }

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        public async Task<uint> WriteInboxStream(InternalDriveFileId file, string extension, Stream stream)
        {
            shared.EnsureDirectoryExists(_tenantPathManager.GetDriveInboxPath(file.DriveId));
            string path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return await shared.WriteStreamAsync(path, stream);
        }

        public Task CleanupInboxFiles(InternalDriveFileId file, List<PayloadDescriptor> descriptors)
        {
            logger.LogDebug("CleanupInboxFiles called - file: {file}", file);

            CleanupInboxFilesInternal(file, descriptors);

            //TODO: the extensions should be centralized
            string[] additionalFiles =
            [
                _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, TenantPathManager.MetadataExtension),
                _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, TenantPathManager.TransferInstructionSetExtension)
            ];

            foreach (var f in additionalFiles)
            {
                logger.LogDebug("CleanupInboxFiles Deleting additional File: {file}", f);
            }

            // clean up the transfer header and metadata since we keep those in the inbox
            shared.DeleteFiles(additionalFiles);

            return Task.CompletedTask;
        }

        private void CleanupInboxFilesInternal(InternalDriveFileId file, List<PayloadDescriptor> descriptors)
        {
            try
            {
                if (!descriptors?.Any() ?? false)
                {
                    return;
                }

                var targetFiles = new List<string>();

                descriptors!.ForEach(descriptor =>
                {
                    var payloadExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(descriptor.Key, descriptor.Uid);
                    string payloadDirectoryAndFilename = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, payloadExtension);
                    targetFiles.Add(payloadDirectoryAndFilename);

                    descriptor.Thumbnails?.ForEach(thumb =>
                    {
                        var thumbnailExtension = TenantPathManager.GetThumbnailFileNameAndExtension(descriptor.Key,
                            descriptor.Uid,
                            thumb.PixelWidth,
                            thumb.PixelHeight);
                        string thumbnailDirectoryAndFilename = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, thumbnailExtension);
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
    }
}
