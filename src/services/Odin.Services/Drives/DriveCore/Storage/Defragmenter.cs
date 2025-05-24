using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

        // Copied from another class
        private async Task<List<string>> GetMissingPayloadsAsync(FileMetadata metadata)
        {
            var missingPayloads = new List<string>();
            var drive = await driveManager.GetDriveAsync(metadata.File.DriveId);

            // special exception *eye roll*.  really need to root this feed thing out of the core
            if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
            {
                return missingPayloads;
            }

            var fileId = metadata.File.FileId;
            foreach (var payloadDescriptor in metadata.Payloads ?? [])
            {
                bool payloadExists = longTermStorageManager.PayloadExistsOnDisk(drive, fileId, payloadDescriptor);
                if (!payloadExists)
                {
                    missingPayloads.Add(TenantPathManager.GetPayloadFileName(fileId, payloadDescriptor.Key, payloadDescriptor.Uid));
                }

                foreach (var thumbnailDescriptor in payloadDescriptor.Thumbnails ?? [])
                {
                    var thumbExists = longTermStorageManager.ThumbnailExistsOnDisk(drive, fileId, payloadDescriptor, thumbnailDescriptor);
                    if (!thumbExists)
                    {
                        missingPayloads.Add(TenantPathManager.GetThumbnailFileName(fileId, payloadDescriptor.Key, payloadDescriptor.Uid, thumbnailDescriptor.PixelWidth,
                            thumbnailDescriptor.PixelHeight));
                    }
                }
            }

            return missingPayloads;
        }

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
            var driveId = await driveManager.GetDriveIdByAliasAsync(targetDrive, true);

            await CheckDriveFileIntegrity(targetDrive, fs, odinContext);


            await VerifyFolder(driveId.GetValueOrDefault(), fs, odinContext);

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
                Ordering = QueryBatchOrdering.Default,
                Sorting = QueryBatchType.FileId
            };

            if (driveFileReaderWriter != null)
                await Task.Delay(0); // NOP to avoid warning. Delete when class is finished

            var driveId = await driveManager.GetDriveIdByAliasAsync(targetDrive, true);
<<<<<<< Updated upstream
            var storageDrive = await driveManager.GetDriveAsync(driveId.GetValueOrDefault());
<<<<<<< Updated upstream

=======
=======

>>>>>>> Stashed changes
>>>>>>> Stashed changes
            var batch = await fs.Query.GetBatch(driveId.GetValueOrDefault(), query, options, odinContext);

            logger.LogDebug("Defragmenting drive {driveName}.  File count: {fc}", driveId, batch.SearchResults.Count());

            foreach (var header in batch.SearchResults)
            {
                var missing = await this.DefragmentFileAsync(driveId.GetValueOrDefault(), header.FileId, fs, odinContext);
                if (missing != null)
                    logger.LogDebug($"Missing payloads for FileID {header.FileId.ToString()}: {missing}");

                // Future improvement: Compare byte-sizes in header to bytes on disk
            }
        }

        /// <summary>
        /// Checks a file for payload integrity.
        /// </summary>
        /// <returns>null if file is complete, otherwise returns string of missing payloads / thumbnails</returns>
        public async Task<string> DefragmentFileAsync(Guid driveId, Guid fileId, IDriveFileSystem fs, IOdinContext odinContext)
        {
            var file = new InternalDriveFileId(driveId, fileId);
            var header = await fs.Storage.GetServerFileHeader(file, odinContext);

<<<<<<< Updated upstream
=======
<<<<<<< Updated upstream
            if (!CheckFileIntegrity(drive, header))
            {
                // We got an incomplete header with a missing payload / thumbnail - what should we do?
                return false;
            }
>>>>>>> Stashed changes

            // XXX DriveStorageServiceBase.AssertPayloadsExistOnFileSystem(FileMetadata metadata)

            // XXX var ops = OrphanTestUtils.GetOrphanedPayloads()

            return true;
        }

<<<<<<< Updated upstream
=======
        private bool CheckFileIntegrity(StorageDrive drive, ServerFileHeader header)
        {
            var fileId = header.FileMetadata.File.FileId;
            var payloads = header.FileMetadata.Payloads;

            foreach (var payload in payloads)
            {
                if (!PayloadExistsOnDisk(drive, fileId, payload))
                    return false;

                foreach (var thumb in payload.Thumbnails ?? [])
                {
                    if (!ThumbnailExistsOnDisk(drive, fileId, payload, thumb))
                        return false;
                }
            }

            return true;
        }

        private void CleanOrphanedPayloadsAndThumbnails(StorageDrive drive, ServerFileHeader header)
        {
            var fileId = header.FileMetadata.File.FileId;
            var payloads = header.FileMetadata.Payloads.ToList();

            var payloadDir = GetPayloadPath(drive, fileId);
            var searchPattern = GetPayloadSearchMask(fileId);
            var files = GetFilesInDirectory(payloadDir, searchPattern,
                24); // Get all files matching fileId-*-* but they must be at least 24 hours old
            var orphans = GetOrphanedPayloads(files, payloads);

            foreach (var orphan in orphans)
            {
                HardDeletePayloadFile(drive, fileId, orphan.Key, orphan.Uid);
            }
        }

=======
            var sl = await GetMissingPayloadsAsync(header.FileMetadata);

            if (sl?.Count > 0)
                return string.Join(",", sl);

            return null;
        }

>>>>>>> Stashed changes

>>>>>>> Stashed changes
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
<<<<<<< Updated upstream

        private InternalDriveFileId GetInternalFile(StorageDrive drive, Guid fileId)
        {
            return new InternalDriveFileId()
            {
                FileId = fileId,
                DriveId = drive.Id
            };
        }
=======
>>>>>>> Stashed changes
    }
}