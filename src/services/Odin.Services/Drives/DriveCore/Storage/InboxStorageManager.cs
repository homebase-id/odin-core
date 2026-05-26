using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Storage
{
    /// <summary>
    /// Temporary storage for a given drive. Used to stage incoming file parts from peer transfers (inbox).
    /// Backed by disk or S3 via IInboxReaderWriter (selected by config).
    /// </summary>
    public class InboxStorageManager(
        IInboxReaderWriter inboxReaderWriter,
        ILogger<InboxStorageManager> logger,
        TenantContext tenantContext)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        public Task<bool> InboxFileExists(InternalDriveFileId file, string extension)
        {
            string path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return inboxReaderWriter.FileExistsAsync(path);
        }

        public async Task<byte[]> GetAllInboxFileBytes(InternalDriveFileId file, string extension)
        {
            string path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return await inboxReaderWriter.GetFileBytesAsync(path);
        }

        public async Task<uint> WriteInboxStream(InternalDriveFileId file, string extension, Stream stream)
        {
            await inboxReaderWriter.EnsureDirectoryAsync(_tenantPathManager.GetDriveInboxPath(file.DriveId));
            string path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return await inboxReaderWriter.WriteStreamAsync(path, stream);
        }

        // Every staged part for a fileId shares the prefix "<driveInboxPath>/<fileId:N>." — deleting by
        // that prefix is the authoritative cleanup, independent of which descriptors the in-flight
        // processor parsed (see the previous glob-based implementation's rationale).
        public async Task CleanupInboxFiles(InternalDriveFileId file)
        {
            logger.LogDebug("CleanupInboxFiles called - file: {file}", file);

            try
            {
                var prefix = _tenantPathManager.GetDriveInboxFilePrefix(file.DriveId, file.FileId);
                await inboxReaderWriter.DeleteByPrefixAsync(prefix);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failure while cleaning up inbox files for {file}", file);
            }
        }
    }
}
