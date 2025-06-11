using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp.Css.Dom;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.DriveCore.Storage.Gugga
{
    public class Defragmenter(
        ILogger<Defragmenter> logger,
        DriveManager driveManager,
        LongTermStorageManager longTermStorageManager,
        TenantContext tenantContext
    )
    {
        private TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

        private bool HasHeaderPayload(FileMetadata header, ParsedPayloadFileRecord fileRecord)
        {
            if (header.File.FileId != fileRecord.FileId)
                return false;

            foreach (var record in header.Payloads)
                if ((record.Key == fileRecord.Key) && (record.Uid.uniqueTime == fileRecord.Uid.uniqueTime))
                    return true;

            return false;
        }


        private bool HasHeaderThumbnail(FileMetadata header, ParsedThumbnailFileRecord fileRecord)
        {
            if (header.File.FileId != fileRecord.FileId)
                return false;

            foreach (var record in header.Payloads)
                if ((record.Key == fileRecord.Key) && (record.Uid.uniqueTime == fileRecord.Uid.uniqueTime))
                    foreach (var thumbnail in record.Thumbnails)
                        if (thumbnail.PixelWidth == fileRecord.Width && thumbnail.PixelHeight == fileRecord.Height)
                            return true;

            return false;
        }

        public async Task<FileMetadata> GetHeader(Dictionary<Guid,FileMetadata> cache, Guid driveId, Guid fileId, IDriveFileSystem fs, IOdinContext odinContext)
        {
            if (cache.ContainsKey(fileId))
            {
                return cache[fileId]; // May return null, which means the recond wasn't in the DB
            }

            var header = await fs.Storage.GetServerFileHeader(new InternalDriveFileId(driveId, fileId), odinContext);
            if (header == null)
                cache.Add(fileId, null);
            else
                cache.Add(fileId, header.FileMetadata);
            return cache[fileId];
        }


        public async Task VerifyFolder(Guid driveId, IDriveFileSystem fs, IOdinContext odinContext)
        {
            var headerCache = new Dictionary<Guid, FileMetadata>();

            var rootpath = _tenantPathManager.GetDrivePayloadPath(driveId);

            for (int first = 0; first < 16; first++)
            {
                for (int second = 0; second < 16; second++)
                {
                    var nibblepath = Path.Combine(first.ToString("x"), second.ToString("x"));
                    var dirpath = Path.Combine(rootpath, nibblepath);
                    var files = GetFilesInDirectory(dirpath, "*.*", 24);

                    if (files == null)
                        continue;

                    foreach (var file in files)
                    {
                        var fileType = TenantPathManager.ParseFileType(file);
                        Guid fileId = Guid.Empty;
                        ParsedPayloadFileRecord parsedFile = null;
                        ParsedThumbnailFileRecord parsedThumb = null;

                        switch (fileType)
                        {
                            case TenantPathManager.FileType.Payload:
                                parsedFile = TenantPathManager.ParsePayloadFilename(file);
                                fileId = parsedFile.FileId;
                                break;
                            case TenantPathManager.FileType.Thumbnail:
                                parsedThumb = TenantPathManager.ParseThumbnailFilename(file);
                                fileId = parsedThumb.FileId;
                                break;
                            case TenantPathManager.FileType.Invalid:
                                logger.LogDebug($"Invalid file {file} in {dirpath}");
                                continue;
                        }

                        if (TenantPathManager.GetPayloadDirectoryFromGuid(fileId) != nibblepath)
                        {
                            logger.LogDebug($"File placed in incorrect directory {file} in {dirpath}");
                            continue;
                        }

                        var header = await GetHeader(headerCache, driveId, fileId, fs, odinContext);

                        if (header == null)
                        {
                            logger.LogDebug($"FileId {fileId} is on disk but not in database, delete.");
                            // Delete (move) this file
                            continue;
                        }

                        if (fileType == TenantPathManager.FileType.Payload)
                        {
                            if (!HasHeaderPayload(header, parsedFile))
                            {
                                logger.LogDebug($"File {file} is not present in the header, marked for deletion");
                                // Move for deletion
                                continue;
                            }
                        }

                        if (fileType == TenantPathManager.FileType.Thumbnail)
                        {
                            if (!HasHeaderThumbnail(header, parsedThumb))
                            {
                                logger.LogDebug($"Thumb {file} is not present in the database header, marked for deletion");
                                // Move for deletion
                                continue;
                            }
                        }

                    }

                }
            }
        }


        /// <summary>
        /// Queries all files on the drive and ensures payloads and thumbnails are as they should be
        /// </summary>
        public async Task Defragment(TargetDrive targetDrive, IDriveFileSystem fs, IOdinContext odinContext)
        {
            var driveId = targetDrive.Alias;

            await CheckDriveFileIntegrity(targetDrive, fs, odinContext);

            await VerifyFolder(driveId, fs, odinContext);

            // VerifyInbox()...
        }

        private async Task FakeIt(IDriveFileSystem fs, IOdinContext odinContext)
        {
            OdinConfiguration config = new OdinConfiguration()
            {
                Host = new OdinConfiguration.HostSection()
                {
                    TenantDataRootPath = "c:\\temp\\odin\\"
                }
            };

            // Fake Michael's identity
            var fakePathManager = new TenantPathManager(config, Guid.Parse("c1b588ba-8971-46e1-b8bd-105999fa8ddb"));

            await VerifyFolder(Guid.Parse("35531928375d4bef8e250c419a8e870d"), fs, odinContext);
        }

        /// <summary>
        /// Queries all files on the drive and ensures payloads and thumbnails are as they should be
        /// </summary>
        public async Task CheckDriveFileIntegrity(TargetDrive targetDrive, IDriveFileSystem fs, IOdinContext odinContext)
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

            var batch = await fs.Query.GetBatch(driveId, query, options, odinContext);

            logger.LogDebug("Defragmenting drive {driveName}.  File count: {fc}", driveId, batch.SearchResults.Count());

            foreach (var header in batch.SearchResults)
            {
                var missing = await this.DefragmentFileAsync(storageDrive, header.FileId, fs, odinContext);
                if (missing != null)
                    logger.LogDebug(missing);

                // Now check for orphaned files?
            }
        }

        /// <summary>
        /// Checks a file for payload integrity.
        /// </summary>
        /// <returns>null if file is complete, otherwise returns string of missing payloads / thumbnails</returns>
        public async Task<string> DefragmentFileAsync(StorageDrive drive, Guid fileId, IDriveFileSystem fs, IOdinContext odinContext)
        {
            var file = new InternalDriveFileId(drive.Id, fileId);
            var header = await fs.Storage.GetServerFileHeader(file, odinContext);

            var sl = await CheckPayloadsIntegrity(drive, header);

            if (sl != null)
                return $"The following files are missing: {string.Join(",", sl)}";

            return null;
        }

        /// <summary>
        /// Returns null if file is OK, otherwise returns the list of missing payloads / thumbnails as full filename plus directory
        /// </summary>
        private async Task<List<string>> CheckPayloadsIntegrity(StorageDrive drive, ServerFileHeader header)
        {
            var fileId = header.FileMetadata.File.FileId;
            var payloads = header.FileMetadata.Payloads;
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

        private void CleanOrphanedPayloadsAndThumbnails(StorageDrive drive, ServerFileHeader header)
        {
            var fileId = header.FileMetadata.File.FileId;
            var payloads = header.FileMetadata.Payloads.ToList();

            var payloadDir = tenantContext.TenantPathManager.GetPayloadDirectory(drive.Id, fileId);
            var searchPattern = OrphanTestUtil.GetPayloadSearchMask(fileId);
            var orphans = GetFilesInDirectory(payloadDir, searchPattern, 24); // Get all files matching fileId-*-* but they must be at least 24 hours old

            foreach (var orphan in orphans)
            {
                // File.Delete(tenantContext.TenantPathManager.GetPayloadDirectoryAndFileName(orphan drive, fileId, orphan.Key, orphan.Uid);
            }
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
