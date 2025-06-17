using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public class Defragmenter(
        ILogger<Defragmenter> logger,
        DriveManager driveManager,
        LongTermStorageManager longTermStorageManager,
        TenantContext tenantContext,
        IdentityDatabase identityDatabase
    )
    {
        private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        private bool HasHeaderPayload(FileMetadata header, ParsedPayloadFileRecord fileRecord)
        {
            if (header.File.FileId != fileRecord.FileId)
                return false;

            foreach (var record in header.Payloads ?? [])
                if ((record.Key == fileRecord.Key) && (record.Uid.uniqueTime == fileRecord.Uid.uniqueTime))
                    return true;

            return false;
        }

        private bool HasHeaderThumbnail(FileMetadata header, ParsedThumbnailFileRecord fileRecord)
        {
            if (header.File.FileId != fileRecord.FileId)
                return false;

            foreach (var record in header.Payloads ?? [])
                if ((record.Key == fileRecord.Key) && (record.Uid.uniqueTime == fileRecord.Uid.uniqueTime))
                    foreach (var thumbnail in record.Thumbnails ?? [])
                        if (thumbnail.PixelWidth == fileRecord.Width && thumbnail.PixelHeight == fileRecord.Height)
                            return true;

            return false;
        }

        public async Task<FileMetadata> GetHeader(Dictionary<Guid,FileMetadata> cache, Guid driveId, Guid fileId)
        {
            if (cache.ContainsKey(fileId))
            {
                return cache[fileId]; // May return null, which means the recond wasn't in the DB
            }

            var record = await identityDatabase.DriveMainIndex.GetAsync(driveId, fileId);

            if (record == null)
                cache.Add(fileId, null);
            else
            {
                var serverFileHeader = ServerFileHeader.FromDriveMainIndexRecord(record);
                cache.Add(fileId, serverFileHeader.FileMetadata);
            }
            return cache[fileId];
        }

        public async Task VerifyInboxDiskFolder(Guid driveId, bool cleanup)
        {
            var validExtensions = new[] { ".metadata", ".transferkeyheader", ".payload", ".thumb" };
            var rootpath = _tenantPathManager.GetDriveInboxPath(driveId);
            var files = GetFilesInDirectory(rootpath, "*", 0);
            if (files == null)
                return;

            //CORRUPT INBOX FILES WITH THIS CODE
            //if (Random.Shared.NextDouble() < 0.5)
            //{
            //    var s = files[0];
            //    s = s+"junk";
            //    FileTouch(s);
            //    files = files.Concat(new[] { s }).ToArray();
            //}

            var (inboxEntries, _) = await identityDatabase.Inbox.PagingByRowIdAsync(int.MaxValue, null);

            logger.LogDebug($"Inbox {driveId} contains {files.Count()} files and {inboxEntries.Count()} inbox table entries");

            foreach (var fileAndDirectory in files)
            {
                var fileName = Path.GetFileName(fileAndDirectory);
                var extension = Path.GetExtension(fileName);
                if (validExtensions.Contains(extension) == false)
                {
                    logger.LogError($"Unable to recognize inbox filename extension {fileName}");
                    continue;
                }

                var fileParts = fileName.Split('.');
                Guid fileId;

                try
                {
                    fileId = new Guid(fileParts[0]);
                }
                catch
                {
                    logger.LogError($"Unable to parse inbox filename GUID portion {fileName}");
                    continue;
                }

                bool exists = inboxEntries.Any(record => record.fileId == fileId && record.boxId == driveId);

                if (exists)
                    continue;

                logger.LogDebug($"Inbox filename {fileName} not in the inbox - deleting if in cleanup");

                // Not confident here yet :-D haven't covered it in a test
                if (cleanup)
                    File.Delete(fileAndDirectory);
            }
        }

        public async Task VerifyPayloadsDiskFolder(Guid driveId, bool cleanup)
        {
            var headerCache = new Dictionary<Guid, FileMetadata>();

            var rootpath = _tenantPathManager.GetDrivePayloadPath(driveId);

            for (int first = 0; first < 16; first++)
            {
                for (int second = 0; second < 16; second++)
                {
                    // Find kataloger der er for meget?

                    var nibblepath = Path.Combine(first.ToString("x"), second.ToString("x"));
                    var dirpath = Path.Combine(rootpath, nibblepath);
                    var files = GetFilesInDirectory(dirpath, "*", 24);

                    if (files == null)
                        continue;

                    foreach (var fileAndDirectory in files)
                    {
                        var fileName = Path.GetFileName(fileAndDirectory);
                        var fileType = TenantPathManager.ParseFileType(fileName);
                        Guid fileId = Guid.Empty;
                        ParsedPayloadFileRecord parsedFile = null;
                        ParsedThumbnailFileRecord parsedThumb = null;

                        switch (fileType)
                        {
                            case TenantPathManager.FileType.Payload:
                                parsedFile = TenantPathManager.ParsePayloadFilename(fileName);
                                fileId = parsedFile.FileId;
                                break;
                            case TenantPathManager.FileType.Thumbnail:
                                parsedThumb = TenantPathManager.ParseThumbnailFilename(fileName);
                                fileId = parsedThumb.FileId;
                                break;
                            case TenantPathManager.FileType.Invalid:
                                logger.LogDebug($"Extension {fileAndDirectory}");
                                if (cleanup)
                                    File.Delete(fileAndDirectory);
                                continue;
                        }

                        if (TenantPathManager.GetPayloadDirectoryFromGuid(fileId) != nibblepath)
                        {
                            logger.LogDebug($"Directory {fileAndDirectory} in {dirpath}");
                            if (cleanup)
                                File.Delete(fileAndDirectory);
                            continue;
                        }

                        var header = await GetHeader(headerCache, driveId, fileId);

                        if (header == null)
                        {
                            logger.LogDebug($"OrphanHeader {fileId}");
                            if (cleanup)
                                File.Delete(fileAndDirectory);
                            continue;
                        }

                        if (fileType == TenantPathManager.FileType.Payload)
                        {
                            if (!HasHeaderPayload(header, parsedFile))
                            {
                                logger.LogDebug($"OrphanPayload {fileAndDirectory}");
                                if (cleanup)
                                    File.Delete(fileAndDirectory);
                                continue;
                            }
                        }

                        if (fileType == TenantPathManager.FileType.Thumbnail)
                        {
                            if (!HasHeaderThumbnail(header, parsedThumb))
                            {
                                logger.LogDebug($"OrphanThumb {fileAndDirectory}");
                                if (cleanup)
                                    File.Delete(fileAndDirectory);
                                continue;
                            }
                        }

                    }

                }
            }
        }

        void FileTouch(string pathAndName)
        {
            string directory = Path.GetDirectoryName(pathAndName);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(pathAndName))
            {
                File.SetLastWriteTime(pathAndName, DateTime.Now);
            }
            else
            {
                File.Create(pathAndName).Dispose();
            }
        }

        /// <summary>
        /// Queries all files on the drive and ensures payloads and thumbnails are as they should be
        /// </summary>
        public async Task Defragment(TargetDrive targetDrive, bool cleanup = false)
        {
            var driveId = targetDrive.Alias;

            //
            // Insert Three Junk Files
            //
            //var f1 = Guid.NewGuid();
            //var s1 = tenantContext.TenantPathManager.GetPayloadDirectoryAndFileName(driveId, f1, "junk", UnixTimeUtcUnique.Now());
            //FileTouch(s1);
            //var s2 = tenantContext.TenantPathManager.GetThumbnailDirectoryAndFileName(driveId, f1, "junk", UnixTimeUtcUnique.Now(), 10, 10);
            //FileTouch(s2);
            //s2 = s2 + ".junk";
            //FileTouch(s2);

            await CheckDrivePayloadsIntegrity(targetDrive);

            await VerifyPayloadsDiskFolder(driveId, cleanup);

            await VerifyInboxDiskFolder(driveId, cleanup);
        }

        /// <summary>
        /// Queries all files on the drive and ensures payloads and thumbnails are as they should be
        /// </summary>
        public async Task CheckDrivePayloadsIntegrity(TargetDrive targetDrive)
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

            var driveId = targetDrive.Alias;
            var storageDrive = await driveManager.GetDriveAsync(driveId);

            var batch = await identityDatabase.DriveMainIndex.GetAllByDriveIdAsync(driveId);

            logger.LogDebug("Defragmenting drive {driveName}.", driveId);

            int missingCount = 0;

            foreach (var header in batch)
            {
                var missing = await this.VerifyFileAsync(storageDrive, header);
                if (missing != null)
                {
                    logger.LogDebug(missing);
                    missingCount++;
                }

                // Now check for orphaned files?
            }

            logger.LogDebug("Defragmenting drive {driveName} summary.  File count: {fc}  Missing count: {mc}", driveId, batch.Count(), missingCount);
        }

        /// <summary>
        /// Checks a file for payload integrity.
        /// </summary>
        /// <returns>null if file is complete, otherwise returns string of missing payloads / thumbnails</returns>
        public async Task<string> VerifyFileAsync(StorageDrive drive, DriveMainIndexRecord record)
        {
            var file = new InternalDriveFileId(drive.Id, record.fileId);
            var serverFileHeader = ServerFileHeader.FromDriveMainIndexRecord(record);

            // CORRUPT HEADERS WITH THIS CODE
            // 
            //if (Random.Shared.NextDouble() < 0.5)
            //{
            //    foreach (var payload in header.FileMetadata.Payloads)
            //    {
            //        File.Delete(tenantContext.TenantPathManager.GetPayloadDirectoryAndFileName(drive.Id, fileId, payload.Key, payload.Uid));
            //        foreach (var thumb in payload.Thumbnails)
            //            File.Delete(tenantContext.TenantPathManager.GetThumbnailDirectoryAndFileName(drive.Id, fileId, payload.Key, payload.Uid, thumb.PixelWidth, thumb.PixelHeight));
            //    }
            //}

            var sl = await CheckPayloadsIntegrity(drive, serverFileHeader);

            if (sl != null)
            {
                var missingTime = SequentialGuid.ToUnixTimeUtc(record.fileId);
                return $"{record.fileId.ToString()} dated {missingTime.ToDateTime():yyyy-MM-dd} following files are missing: {string.Join(",", sl)}";
            }

            return null;
        }

        /// <summary>
        /// Returns null if file is OK, otherwise returns the list of missing payloads / thumbnails as full filename plus directory
        /// </summary>
        private async Task<List<string>> CheckPayloadsIntegrity(StorageDrive drive, ServerFileHeader header)
        {
            var fileId = header.FileMetadata.File.FileId;
            var payloads = header.FileMetadata?.Payloads ?? [];
            var sl = new List<string>();

            // Future improvement: Compare byte-sizes in header to bytes on disk

            foreach (var payload in payloads)
            {
                if (!await longTermStorageManager.PayloadExistsOnDiskAsync(drive, fileId, payload))
                    sl.Add(tenantContext.TenantPathManager.GetPayloadDirectoryAndFileName(drive.Id, fileId, payload.Key, payload.Uid));

                foreach (var thumb in payload.Thumbnails ?? [])
                {
                    if (!await longTermStorageManager.ThumbnailExistsOnDiskAsync(drive, fileId, payload, thumb))
                        sl.Add(tenantContext.TenantPathManager.GetThumbnailDirectoryAndFileName(drive.Id, fileId, payload.Key, payload.Uid, thumb.PixelWidth, thumb.PixelHeight));
                }
            }

            if (sl.Count > 0)
                return sl;
            else
                return null;
        }

        /// <summary>
        /// Get files matching the pattern that are at least minAgeHours old
        /// We don't want to risk getting recent files
        /// </summary>
        /// <param name="dir"></param>
        /// <param name="searchPattern"></param>
        /// <param name="minAgeHours"></param>
        /// <returns></returns>
        private string[] GetFilesInDirectory(string dir, string searchPattern, int minAgeHours)
        {
            var directory = new DirectoryInfo(dir);

            if (directory.Exists == false)
                return null; // Maybe create it...

            var files = directory.GetFiles(searchPattern)
                .Where(f => f.CreationTimeUtc <= DateTime.UtcNow.AddHours(-minAgeHours))
                .Select(f => f.FullName)
                .ToArray();
            return files;
        }

    }
}
