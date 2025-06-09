using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.DriveCore.Storage.Gugga
{
    public class Defragmenter(
        ILogger<Defragmenter> logger,
        IDriveManager driveManager,
        DriveFileReaderWriter driveFileReaderWriter)
    {

        public static Guid RestoreFileIdFromDiskString(string fileId)
        {
            if (fileId.Length != 32)
                throw new ArgumentException("Invalid fileId length for restoration; expected 32 characters.");

            // Convert hex string to byte array (2 chars = 1 byte)
            byte[] bytes = Enumerable.Range(0, 16)
                .Select(i => Convert.ToByte(fileId.Substring(i * 2, 2), 16))
                .ToArray();

            return new Guid(bytes);
        }

        public async Task VerifyFolder(StorageDrive drive, string folderPath, IDriveFileSystem fs, IOdinContext odinContext)
        {
            var files = GetFilesInDirectory(folderPath, "*.*", 24);

            var fileIds = files
                .Select(f => Path.GetFileNameWithoutExtension(f).Split(TenantPathManager.PayloadDelimiter)[0])
                .Distinct()
                .Select(f => Guid.TryParse(RestoreFileIdFromDiskString(f).ToString(), out var guid) ? guid : (Guid?)null)
                .Where(g => g.HasValue)
                .Select(g => g.Value)
                .ToList();

            foreach (var fileId in fileIds)
            {
                // var header = await GetServerFileHeader(drive, fileId, fst);
                var file = GetInternalFile(drive, fileId);
                var header = await fs.Storage.GetServerFileHeader(file, odinContext);

                if (header == null)
                {
                    // We have a file on disk with no corresponding entry in the DB
                    // We should probably rename it ...
                }
            }
        }

        /// <summary>
        /// Queries all files on the drive and defrags
        /// </summary>
        public async Task DefragDrive(TargetDrive targetDrive, IDriveFileSystem fs, IOdinContext odinContext)
        {
            var query = new FileQueryParams
            {
                TargetDrive = targetDrive,
                FileType = null,
                FileState = null,
                DataType = null,
                ArchivalStatus = null,
                Sender = null,
                GroupId = null,
                UserDate = null,
                ClientUniqueIdAtLeastOne = null,
                TagsMatchAtLeastOne = null,
                TagsMatchAll = null,
                LocalTagsMatchAtLeastOne = null,
                LocalTagsMatchAll = null,
                GlobalTransitId = null
            };

            var options = new QueryBatchResultOptions
            {
                MaxRecords = int.MaxValue,
                IncludeHeaderContent = false,
                ExcludePreviewThumbnail = true,
                ExcludeServerMetaData = true,
                IncludeTransferHistory = true,
                Cursor = default,
                Ordering = QueryBatchSortOrder.Default,
                Sorting = QueryBatchSortField.CreatedDate
            };

            if (driveFileReaderWriter != null)
                await Task.Delay(0); // NOP to avoid warning. Delete when class is finished

            var driveId = targetDrive.Alias;
            var storageDrive = await driveManager.GetDriveAsync(driveId);

            var batch = await fs.Query.GetBatch(driveId, query, options, odinContext);

            logger.LogDebug("Defragmenting drive {driveName}.  File count: {fc}", storageDrive.Name, batch.SearchResults.Count());
            
            foreach (var header in batch.SearchResults)
            {
                await this.DefragmentFileAsync(storageDrive, header.FileId, fs, odinContext);
            }
        }

        public async Task<bool> DefragmentFileAsync(StorageDrive drive, Guid fileId, IDriveFileSystem fs, IOdinContext odinContext)
        {
            // var header = await GetServerFileHeader(drive, fileId, fst);
            var file = GetInternalFile(drive, fileId);
            var header = await fs.Storage.GetServerFileHeader(file, odinContext);


            // XXX DriveStorageServiceBase.AssertPayloadsExistOnFileSystem(FileMetadata metadata)

            // XXX var ops = OrphanTestUtils.GetOrphanedPayloads()

            return true;
        }

        /// <summary>
        /// Get files matching the pattern that are at least minAgeHours old
        /// We don't want to risk getting recent files
        /// </summary>
        /// <param name="dir"></param>
        /// <param name="searchPattern"></param>
        /// <param name="minAgeHours"></param>
        /// <returns></returns>
        public string[] GetFilesInDirectory(string dir, string searchPattern, int minAgeHours)
        {
            var directory = new DirectoryInfo(dir);
            var files = directory.GetFiles(searchPattern)
                .Where(f => f.CreationTimeUtc <= DateTime.UtcNow.AddHours(-minAgeHours))
                .Select(f => f.FullName)
                .ToArray();
            return files;
        }

        private InternalDriveFileId GetInternalFile(StorageDrive drive, Guid fileId)
        {
            return new InternalDriveFileId()
            {
                FileId = fileId,
                DriveId = drive.Id
            };
        }
    }
}