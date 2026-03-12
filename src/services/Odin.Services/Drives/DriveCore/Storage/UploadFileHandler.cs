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
    /// File handler for upload temporary storage.
    /// </summary>
    public class UploadFileHandler(
        FileReaderWriter fileReaderWriter,
        IDriveManager driveManager,
        ILogger<UploadFileHandler> logger,
        TenantContext tenantContext)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;
        private readonly FileHandlerShared _shared = new(fileReaderWriter, logger);

        public async Task<bool> TempFileExists(TempFile tempFile, string extension)
        {
            string path = await GetTempFilenameAndPathInternal(tempFile, extension);
            return _shared.TempFileExists(path);
        }

        /// <summary>
        /// Gets a stream of data for the specified file
        /// </summary>
        public async Task<byte[]> GetAllFileBytes(TempFile tempFile, string extension)
        {
            string path = await GetTempFilenameAndPathInternal(tempFile, extension);
            return await _shared.GetAllFileBytes(path);
        }

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        public async Task<uint> WriteStream(TempFile tempFile, string extension, Stream stream)
        {
            string path = await GetTempFilenameAndPathInternal(tempFile, extension, true);
            return await _shared.WriteStream(path, stream);
        }



        /// <summary>
        /// Deletes all files matching <param name="tempFile"></param> regardless of extension
        /// </summary>
        public async Task CleanupUploadedTempFiles(TempFile tempFile, List<PayloadDescriptor> descriptors)
        {
            await CleanupTempFilesInternal(tempFile, descriptors);
        }

        private async Task CleanupTempFilesInternal(TempFile tempFile, List<PayloadDescriptor> descriptors)
        {
            try
            {
                if (!descriptors?.Any() ?? false)
                {
                    return;
                }

                var drive = await driveManager.GetDriveAsync(tempFile.File.DriveId);

                var targetFiles = new List<string>();

                // add in transfer history and metadata


                descriptors!.ForEach(descriptor =>
                {
                    var payloadExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(descriptor.Key, descriptor.Uid);
                    string payloadDirectoryAndFilename = GetTempFilenameAndPathInternal(drive, tempFile, payloadExtension);
                    targetFiles.Add(payloadDirectoryAndFilename);

                    descriptor.Thumbnails?.ForEach(thumb =>
                    {
                        var thumbnailExtension = TenantPathManager.GetThumbnailFileNameAndExtension(descriptor.Key,
                            descriptor.Uid,
                            thumb.PixelWidth,
                            thumb.PixelHeight);
                        string thumbnailDirectoryAndFilename = GetTempFilenameAndPathInternal(drive, tempFile, thumbnailExtension);
                        targetFiles.Add(thumbnailDirectoryAndFilename);
                    });
                });

                fileReaderWriter.DeleteFiles(targetFiles);
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

        private string GetUploadFileDirectory(StorageDrive drive, bool ensureExists = false)
        {
            string path = drive.GetDriveUploadPath();

            if (ensureExists)
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }

        private async Task<string> GetTempFilenameAndPathInternal(TempFile tempFile, string extension, bool ensureExists = false)
        {
            var drive = await driveManager.GetDriveAsync(tempFile.File.DriveId);
            return GetTempFilenameAndPathInternal(drive, tempFile, extension, ensureExists);
        }

        private string GetTempFilenameAndPathInternal(StorageDrive drive, TempFile tempFile, string extension, bool ensureExists = false)
        {
            string dir = GetUploadFileDirectory(drive, ensureExists);
            return _shared.GetTempFilenameAndPath(dir, tempFile, extension);
        }
    }
}