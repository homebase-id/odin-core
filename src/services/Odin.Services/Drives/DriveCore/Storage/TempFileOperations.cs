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
    /// Shared utility methods for temporary file operations
    /// </summary>
    public static class TempFileOperations
    {
        public static async Task<uint> WriteStream(FileReaderWriter fileReaderWriter, ILogger logger, string path, Stream stream)
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

        public static async Task CleanupFiles(FileReaderWriter fileReaderWriter, IDriveManager driveManager, ILogger logger,
            InternalDriveFileId fileId, List<PayloadDescriptor> descriptors,
            Func<StorageDrive, InternalDriveFileId, string, string> pathResolver, string fileType)
        {
            try
            {
                if (!descriptors?.Any() ?? false)
                {
                    return;
                }

                var drive = await driveManager.GetDriveAsync(fileId.DriveId);
                var targetFiles = new List<string>();

                descriptors!.ForEach(descriptor =>
                {
                    var payloadExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(descriptor.Key, descriptor.Uid);
                    string payloadDirectoryAndFilename = pathResolver(drive, fileId, payloadExtension);
                    targetFiles.Add(payloadDirectoryAndFilename);

                    descriptor.Thumbnails?.ForEach(thumb =>
                    {
                        var thumbnailExtension = TenantPathManager.GetThumbnailFileNameAndExtension(descriptor.Key,
                            descriptor.Uid,
                            thumb.PixelWidth,
                            thumb.PixelHeight);
                        string thumbnailDirectoryAndFilename = pathResolver(drive, fileId, thumbnailExtension);
                        targetFiles.Add(thumbnailDirectoryAndFilename);
                    });
                });

                fileReaderWriter.DeleteFiles(targetFiles);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failure while cleaning up {type}", fileType);
            }
        }

        public static string GetPathFromDrive(StorageDrive drive, InternalDriveFileId fileId, string extension,
            Func<StorageDrive, string> drivePathGetter, bool ensureExists = false)
        {
            string path = drivePathGetter(drive);
            if (ensureExists)
            {
                Directory.CreateDirectory(path);
            }
            return Path.Combine(path, TenantPathManager.GetFilename(fileId.FileId, extension));
        }
    }
}
