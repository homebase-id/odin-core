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
    /// </summary>
    public class InboxStorageManager(
        FileReaderWriter fileReaderWriter,
        ILogger<InboxStorageManager> logger,
        TenantContext tenantContext)
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        public Task<bool> InboxFileExists(InternalDriveFileId file, string extension)
        {
            string path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return Task.FromResult(fileReaderWriter.FileExists(path));
        }

        /// <summary>
        /// Gets all bytes for the specified file
        /// </summary>
        public async Task<byte[]> GetAllInboxFileBytes(InternalDriveFileId file, string extension)
        {
            string path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return await fileReaderWriter.GetAllFileBytesAsync(path);
        }

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        public async Task<uint> WriteInboxStream(InternalDriveFileId file, string extension, Stream stream)
        {
            fileReaderWriter.CreateDirectory(_tenantPathManager.GetDriveInboxPath(file.DriveId));
            string path = _tenantPathManager.GetDriveInboxFilePath(file.DriveId, file.FileId, extension);
            return await fileReaderWriter.WriteStreamAsync(path, stream);
        }

        // We used to take a List<PayloadDescriptor> and compute each .payload / .thumb path from
        // it. That leaked staging files whenever the inbox processor caught an exception and
        // returned an empty descriptor list (PeerInboxProcessor.ProcessInboxItemAsync's catch arms
        // all return (DeleteFromInbox, [])): the row was MarkComplete'd and the metadata +
        // transferkeyheader were deleted, but the .payload/.thumb files were left behind, surfacing
        // later as inbox orphans. The drive inbox dir is a single-purpose staging area where every
        // file is prefixed with "{fileId:N}." (see TenantPathManager.GetFilename), so a glob on that
        // prefix is the authoritative way to remove everything for a given fileId — independent of
        // whatever descriptors the in-flight processor managed to parse.
        public Task CleanupInboxFiles(InternalDriveFileId file)
        {
            logger.LogDebug("CleanupInboxFiles called - file: {file}", file);

            try
            {
                var dir = _tenantPathManager.GetDriveInboxPath(file.DriveId);
                if (!Directory.Exists(dir))
                {
                    return Task.CompletedTask;
                }

                var prefix = TenantPathManager.GuidToPathSafeString(file.FileId);
                var matches = Directory.GetFiles(dir, prefix + ".*");
                fileReaderWriter.DeleteFiles(matches);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failure while cleaning up inbox files for {file}", file);
            }

            return Task.CompletedTask;
        }
    }
}
