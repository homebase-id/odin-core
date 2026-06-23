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
    /// Backed by local disk or S3 depending on configuration, via <see cref="InboxFileStore"/>.
    /// </summary>
    // TODO:INBOX Delete this entire class once the inbox folder is drained (no pre-upgrade items left in any
    // inbox). Both producers now carry metadata on the inbox row; this only serves draining legacy items.
    public class InboxStorageManager(
        InboxFileStore inboxFileStore,
        ILogger<InboxStorageManager> logger,
        TenantContext tenantContext)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        public Task<bool> InboxFileExists(InternalDriveFileId file, string extension)
        {
            var path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return inboxFileStore.ExistsAsync(path);
        }

        /// <summary>
        /// Gets all bytes for the specified file
        /// </summary>
        public Task<byte[]> GetAllInboxFileBytes(InternalDriveFileId file, string extension)
        {
            var path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return inboxFileStore.ReadAllBytesAsync(path);
        }

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        public async Task<uint> WriteInboxStream(InternalDriveFileId file, string extension, Stream stream)
        {
            var driveDir = _tenantPathManager.GetDriveInboxPath(file.DriveId);
            await inboxFileStore.EnsureDirectoryAsync(driveDir);
            var path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return await inboxFileStore.WriteStreamAsync(path, stream);
        }

        // The drive inbox dir is a single-purpose staging area where every file is prefixed with
        // "{fileId:N}." (see TenantPathManager.GetFilename). Cleanup removes that whole set for the
        // fileId — independent of whatever descriptors an in-flight processor managed to parse —
        // which prevents the .payload/.thumb orphan leak that a descriptor-driven cleanup caused.
        public async Task CleanupInboxFiles(InternalDriveFileId file)
        {
            logger.LogDebug("CleanupInboxFiles called - file: {file}", file);
            try
            {
                var driveDir = _tenantPathManager.GetDriveInboxPath(file.DriveId);
                await inboxFileStore.DeleteSetAsync(driveDir, file.FileId);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failure while cleaning up inbox files for {file}", file);
            }
        }
    }
}
