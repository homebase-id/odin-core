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
    /// Temporary storage for a given drive. Used to stage incoming file parts from uploads.
    /// </summary>
    public class UploadStorageManager(
        FileHandlerShared shared,
        ILogger<UploadStorageManager> logger,
        TenantContext tenantContext)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        public Task<bool> UploadFileExists(InternalDriveFileId file, string extension)
        {
            string path = _tenantPathManager.GetDriveUploadFilePath(file.DriveId, file.FileId, extension);
            return Task.FromResult(shared.FileExists(path));
        }

        /// <summary>
        /// Gets all bytes for the specified file
        /// </summary>
        public async Task<byte[]> GetAllUploadFileBytes(InternalDriveFileId file, string extension)
        {
            string path = _tenantPathManager.GetDriveUploadFilePath(file.DriveId, file.FileId, extension);
            return await shared.GetAllFileBytesAsync(path);
        }

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        public async Task<uint> WriteUploadStream(InternalDriveFileId file, string extension, Stream stream)
        {
            shared.EnsureDirectoryExists(_tenantPathManager.GetDriveUploadPath(file.DriveId));
            string path = _tenantPathManager.GetDriveUploadFilePath(file.DriveId, file.FileId, extension);
            return await shared.WriteStreamAsync(path, stream);
        }

        /// <summary>
        /// Deletes all files matching <param name="file"></param> regardless of extension
        /// </summary>
        public Task CleanupUploadFiles(InternalDriveFileId file, List<PayloadDescriptor> descriptors)
        {
            CleanupUploadFilesInternal(file, descriptors);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets the physical path of the specified file
        /// </summary>
        public Task<string> GetUploadPath(InternalDriveFileId file, string extension)
        {
            return Task.FromResult(_tenantPathManager.GetDriveUploadFilePath(file.DriveId, file.FileId, extension));
        }

        private void CleanupUploadFilesInternal(InternalDriveFileId file, List<PayloadDescriptor> descriptors)
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
                    string payloadDirectoryAndFilename = _tenantPathManager.GetDriveUploadFilePath(file.DriveId, file.FileId, payloadExtension);
                    targetFiles.Add(payloadDirectoryAndFilename);

                    descriptor.Thumbnails?.ForEach(thumb =>
                    {
                        var thumbnailExtension = TenantPathManager.GetThumbnailFileNameAndExtension(descriptor.Key,
                            descriptor.Uid,
                            thumb.PixelWidth,
                            thumb.PixelHeight);
                        string thumbnailDirectoryAndFilename = _tenantPathManager.GetDriveUploadFilePath(file.DriveId, file.FileId, thumbnailExtension);
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
    }
}
