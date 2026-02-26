using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.DriveCore.Storage
{
/// <summary>
/// Handles storage operations for inbox files.
/// Inbox files are stored under the drive's storage path (local or S3 depending on configuration, e.g., drives/{drive-id}/inbox or S3 equivalent)
/// rather than the temporary path, as they represent staged incoming transfers awaiting processing.
/// </summary>
public class InboxStorage(
        FileReaderWriter fileReaderWriter,
        IDriveManager driveManager,
        ILogger<InboxStorage> logger,
        TenantContext tenantContext)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        public async Task<bool> FileExists(InboxFile file, string extension)
        {
            string path = await GetPathInternal(file, extension);
            return fileReaderWriter.FileExists(path);
        }

        public async Task<byte[]> GetAllFileBytes(InboxFile file, string extension)
        {
            string path = await GetPathInternal(file, extension);
            logger.LogDebug("Getting inbox file bytes for [{path}]", path);
            var bytes = await fileReaderWriter.GetAllFileBytesAsync(path);
            logger.LogDebug("Got {count} bytes from {path}", bytes.Length, path);
            return bytes;
        }

        public async Task<uint> WriteStream(InboxFile file, string extension, Stream stream)
        {
            string path = await GetPathInternal(file, extension, true);
            return await TempFileOperations.WriteStream(fileReaderWriter, logger, path, stream);
        }

        public async Task CleanupInboxFiles(InboxFile file, List<PayloadDescriptor> descriptors)
        {
            logger.LogDebug("CleanupInboxFiles called - file: {file}", file);
            await TempFileOperations.CleanupFiles(fileReaderWriter, driveManager, logger,
                file.FileId, descriptors,
                (drive, fileId, extension) => TempFileOperations.GetPathFromDrive(drive, fileId, extension, d => d.GetDriveInboxStoragePath()),
                "inbox files");

            // Additionally, clean up metadata and transfer instruction set files that are specific to inbox storage
            string[] additionalFiles =
            [
                await GetPathInternal(file, TenantPathManager.MetadataExtension),
                await GetPathInternal(file, TenantPathManager.TransferInstructionSetExtension)
            ];

            foreach (var filePath in additionalFiles)
            {
                logger.LogDebug("CleanupInboxFiles Deleting additional File: {file}", filePath);
            }

            fileReaderWriter.DeleteFiles(additionalFiles);
        }

        public async Task<string> GetPath(InboxFile file, string extension)
        {
            return await GetPathInternal(file, extension);
        }

        private async Task<string> GetPathInternal(InboxFile file, string extension, bool ensureExists = false)
        {
            var drive = await driveManager.GetDriveAsync(file.FileId.DriveId);
            return TempFileOperations.GetPathFromDrive(drive, file.FileId, extension, d => d.GetDriveInboxStoragePath(), ensureExists);
        }
    }
}
