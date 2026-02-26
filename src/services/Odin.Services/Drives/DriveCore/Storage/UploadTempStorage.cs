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
/// Handles storage operations for temporary upload files.
/// Upload files are stored locally under the temporary storage path (e.g., temp/drives/{drive-id}/uploads)
/// for staging during direct-write transfers before moving to long-term storage.
/// These files are kept on the local server until processed, at which point their contents are moved to long-term storage (S3 if enabled).
/// </summary>
public class UploadTempStorage(
        FileReaderWriter fileReaderWriter,
        IDriveManager driveManager,
        ILogger<UploadTempStorage> logger,
        TenantContext tenantContext)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        public async Task<bool> FileExists(UploadFile file, string extension)
        {
            string path = await GetPathInternal(file, extension);
            return fileReaderWriter.FileExists(path);
        }

        public async Task<byte[]> GetAllFileBytes(UploadFile file, string extension)
        {
            string path = await GetPathInternal(file, extension);
            logger.LogDebug("Getting upload file bytes for [{path}]", path);
            var bytes = await fileReaderWriter.GetAllFileBytesAsync(path);
            logger.LogDebug("Got {count} bytes from {path}", bytes.Length, path);
            return bytes;
        }

        public async Task<uint> WriteStream(UploadFile file, string extension, Stream stream)
        {
            string path = await GetPathInternal(file, extension, true);
            return await TempFileOperations.WriteStream(fileReaderWriter, logger, path, stream);
        }

        public async Task CleanupUploadedFiles(UploadFile file, List<PayloadDescriptor> descriptors)
        {
            logger.LogDebug("CleanupUploadedFiles called - file: {file}", file);
            await TempFileOperations.CleanupFiles(fileReaderWriter, driveManager, logger,
                file.FileId, descriptors,
                (drive, fileId, extension) => TempFileOperations.GetPathFromDrive(drive, fileId, extension, d => d.GetDriveUploadPath()),
                "upload files");
        }

        public async Task<string> GetPath(UploadFile file, string extension)
        {
            return await GetPathInternal(file, extension);
        }

        private async Task<string> GetPathInternal(UploadFile file, string extension, bool ensureExists = false)
        {
            var drive = await driveManager.GetDriveAsync(file.FileId.DriveId);
            return TempFileOperations.GetPathFromDrive(drive, file.FileId, extension, d => d.GetDriveUploadPath(), ensureExists);
        }
    }
}
