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
        UploadFileStore uploadFileStore,
        FileReaderWriter fileReaderWriter,
        ILogger<UploadStorageManager> logger,
        TenantContext tenantContext)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        public Task<bool> UploadFileExists(InternalDriveFileId file, string extension)
        {
            string path = _tenantPathManager.GetDriveUploadFilePath(file.DriveId, file.FileId, extension);
            return uploadFileStore.ExistsAsync(path);
        }

        /// <summary>
        /// Gets all bytes for the specified file
        /// </summary>
        public async Task<byte[]> GetAllUploadFileBytes(InternalDriveFileId file, string extension)
        {
            string path = _tenantPathManager.GetDriveUploadFilePath(file.DriveId, file.FileId, extension);
            return await uploadFileStore.ReadAllBytesAsync(path);
        }

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        public async Task<uint> WriteUploadStream(InternalDriveFileId file, string extension, Stream stream)
        {
            // [UploadTiming] Diagnostic: time the LOCAL staging write to prove local disk is fast vs S3 commit.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await uploadFileStore.EnsureDirectoryAsync(_tenantPathManager.GetDriveUploadPath(file.DriveId));
            string path = _tenantPathManager.GetDriveUploadFilePath(file.DriveId, file.FileId, extension);
            var bytesWritten = await uploadFileStore.WriteStreamAsync(path, stream);
            logger.LogInformation(
                "[UploadTiming] LOCAL stage write fileId:{fileId} ext:{ext} bytes:{bytes} elapsedMs:{elapsedMs}",
                file.FileId, extension, bytesWritten, sw.ElapsedMilliseconds);
            return bytesWritten;
        }

        /// <summary>
        /// Deletes all files matching <param name="file"></param> regardless of extension
        /// </summary>
        public Task CleanupUploadFiles(InternalDriveFileId file, List<PayloadDescriptor> descriptors)
        {
            CleanupUploadFilesInternal(file, descriptors);
            return Task.CompletedTask;
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

                fileReaderWriter.DeleteFiles(targetFiles);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failure while cleaning up upload files");
            }
        }
    }
}
