using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3.Model;
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

        public async Task VerifyInboxEntiresIntegrity(Guid driveId, bool cleanup)
        {
            const string logPrefix = "INBOX-INTEGRITY";
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

            logger.LogDebug($"{logPrefix} STATUS - {driveId} contains {files.Count()} files and {inboxEntries.Count()} inbox table entries");

            foreach (var fileAndDirectory in files)
            {
                var fileName = Path.GetFileName(fileAndDirectory);
                var extension = Path.GetExtension(fileName);
                if (validExtensions.Contains(extension) == false)
                {
                    logger.LogError($"{logPrefix} INVALID - unable to recognize inbox filename extension {fileName}");
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
                    logger.LogError($"{logPrefix} INVALID - Unable to parse inbox filename GUID portion {fileName}");
                    continue;
                }

                bool exists = inboxEntries.Any(record => record.fileId == fileId && record.boxId == driveId);

                if (exists)
                    continue;

                logger.LogDebug($"{logPrefix} DELETE - filename not in the inbox - deleting if in cleanup - {fileName}");

                // Not confident here yet :-D haven't covered it in a test
                if (cleanup)
                    File.Delete(fileAndDirectory);
            }
        }

        private void SafeDeleteDirectory(string directory, Guid driveId, bool cleanup)
        {
            // Normalize path and count directories
            string normalizedPath = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            int depth = normalizedPath.Split(Path.DirectorySeparatorChar).Length;

            // Check if path is at least 3 subdirectories deep
            if (depth < 3)
                throw new InvalidOperationException("Directory path is too shallow (less than 3 subdirectories).");

            string driveName = TenantPathManager.GuidToPathSafeString(driveId);

            // Let's make sure that /drives/{driveName} is part of the string
            string expectedPathSegment = $"{Path.DirectorySeparatorChar}drives{Path.DirectorySeparatorChar}{driveName}";
            if (!normalizedPath.Contains(expectedPathSegment, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Directory path '{normalizedPath}' does not contain expected segment '{expectedPathSegment}'");

            if (cleanup)
                Directory.Delete(directory, recursive: true);
        }


        private async Task VerifyDriveDirectories(string rootpath, string logPrefix, bool cleanup)
        {
            var folders = Directory.GetDirectories(rootpath, "*", SearchOption.TopDirectoryOnly);
            if (folders.Length == 0)
                return;

            var (drives, _, _) = await identityDatabase.Drives.GetList(int.MaxValue, null);

            logger.LogDebug($"{logPrefix} STATUS - {folders.Length} directories; {drives.Count} rows");

            foreach (var folder in folders)
            {
                var folderName = new DirectoryInfo(folder).Name;

                Guid folderId;
                try
                {
                    folderId = new Guid(folderName);
                }
                catch
                {
                    logger.LogError($"{logPrefix} INVALID - Unable to parse folder name into a GUID {folderName}");
                    continue;
                }

                bool exists = drives.Any(record => record.DriveId == folderId);

                if (exists)
                    continue;

                logger.LogDebug($"{logPrefix} DELETE - folder not in the Drives table - deleting if in cleanup {folderName}");

                SafeDeleteDirectory(folder, folderId, cleanup);
            }

            /*

            ENABLE WHEN WE CREATE A DIRECTORY AND NIBBLES ON DRIVE CREATION

            foreach (var drive in drives)
            {
                var folder = TenantPathManager.GuidToPathSafeString(drive.DriveId);
                var exists = Directory.Exists(Path.Combine(rootpath, folder));

                if (exists == false)
                    logger.LogError($"{logPrefix} INVALID - Drive folder missing on disk {folder}");
            }
            */
        }

        public async Task VerifyDriveDirectoriesTemp(bool cleanup)
        {
            var rootpath = _tenantPathManager.TempDrivesPath;

            if (Directory.Exists(rootpath))
            {
                await VerifyDriveDirectories(rootpath, "TEMP-DRIVES", cleanup);
            }
        }

        public async Task VerifyDriveDirectoriesPayloads(bool cleanup)
        {
            var rootpath = _tenantPathManager.PayloadsDrivesPath;

            if (Directory.Exists(rootpath))
            {
                await VerifyDriveDirectories(rootpath, "PAYLOADS-DRIVES", cleanup);
            }
        }

        /// <summary>
        /// For each folder on the given drive, checks all the files on the disk.
        /// Makes sure the file belongs to a header in the database
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="cleanup"></param>
        public async Task VerifyPayloadsFilesInDiskFolder(Guid driveId, bool cleanup)
        {
            const string logPrefix = "PAYLOAD-FILE";
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
                                logger.LogDebug($"{logPrefix} INVALID - Extension {fileAndDirectory}");
                                if (cleanup)
                                    File.Delete(fileAndDirectory);
                                continue;
                        }

                        if (TenantPathManager.GetPayloadDirectoryFromGuid(fileId) != nibblepath)
                        {
                            logger.LogDebug($"{logPrefix} DELETE - placed in incorrect nibble directory: {fileAndDirectory} in {dirpath}");
                            if (cleanup)
                                File.Delete(fileAndDirectory);
                            continue;
                        }

                        var header = await GetHeader(headerCache, driveId, fileId);

                        if (header == null)
                        {
                            logger.LogDebug($"{logPrefix} DELETE - no corresponding header in database {fileId}");
                            if (cleanup)
                                File.Delete(fileAndDirectory);
                            continue;
                        }

                        // If the payloads are remote, the file shouldn't be on disk
                        if (header.DataSource?.PayloadsAreRemote == true)
                        {
                            logger.LogDebug($"{logPrefix} DELETE - Payloads are remote but found a file {fileAndDirectory}");
                            if (cleanup)
                                File.Delete(fileAndDirectory);
                            continue;
                        }

                        if (fileType == TenantPathManager.FileType.Payload)
                        {
                            if (!HasHeaderPayload(header, parsedFile))
                            {
                                logger.LogDebug($"{logPrefix} DELETE - header doesn't contain payload {fileAndDirectory}");
                                if (cleanup)
                                    File.Delete(fileAndDirectory);
                                continue;
                            }
                        }

                        if (fileType == TenantPathManager.FileType.Thumbnail)
                        {
                            if (!HasHeaderThumbnail(header, parsedThumb))
                            {
                                logger.LogDebug($"{logPrefix} DELETE - header doesn't contain thumbnail {fileAndDirectory}");
                                if (cleanup)
                                    File.Delete(fileAndDirectory);
                                continue;
                            }
                        }

                    }

                }
            }
        }

        private void ValidateDriveDirectories(Guid driveId, bool createDirs = false)
        {
            const string logPrefix = "ValidateDriveDirs";
            string payloadDirectory = _tenantPathManager.GetDrivePayloadPath(driveId);

            if (Directory.Exists(payloadDirectory) == false)
            {
                logger.LogError($"{logPrefix} MISSING - no such drive directory on disk {payloadDirectory}");

                if (createDirs)
                    Directory.CreateDirectory(payloadDirectory);
            }

            /* FOR NOW WE AGREED TO CREATE NIBBLE DIRECTORIES ON THE FLY

            for (int first = 0; first < 16; first++)
            {
                var firstNibblePath = Path.Combine(payloadDirectory, first.ToString("x"));
                if (Directory.Exists(firstNibblePath) == false)
                {
                    logger.LogError($"{logPrefix} MISSING - no such first nibble directory on disk {firstNibblePath}");

                    if (createDirs)
                        Directory.CreateDirectory(firstNibblePath);
                }

                for (int second = 0; second < 16; second++)
                {
                    var secondNibblePath = Path.Combine(firstNibblePath, second.ToString("x"));
                    if (Directory.Exists(secondNibblePath) == false)
                    {
                        logger.LogError($"{logPrefix} MISSING - no such second nibble directory on disk {secondNibblePath}");

                        if (createDirs)
                            Directory.CreateDirectory(secondNibblePath);
                    }
                }
            }*/

            var uploadPath = _tenantPathManager.GetDriveUploadPath(driveId);
            if (Directory.Exists(uploadPath) == false)
            {
                logger.LogError($"{logPrefix} MISSING - no upload directory on disk {uploadPath}");

                if (createDirs)
                    Directory.CreateDirectory(uploadPath);
            }

            var inboxPath = _tenantPathManager.GetDriveInboxPath(driveId);
            if (Directory.Exists(inboxPath) == false)
            {
                logger.LogError($"{logPrefix} MISSING - no upload directory on disk {inboxPath}");

                if (createDirs)
                    Directory.CreateDirectory(inboxPath);
            }
        }


        /// <summary>
        /// Queries all files on the drive and ensures payloads and thumbnails are as they should be
        /// </summary>
        public async Task Defragment(bool cleanup = false)
        {
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


            await VerifyDriveDirectoriesTemp(cleanup);
            await VerifyDriveDirectoriesPayloads(cleanup);

            var (drives, _, _) = await identityDatabase.Drives.GetList(int.MaxValue, null);
            foreach (var drive in drives)
            {
                ValidateDriveDirectories(drive.DriveId, cleanup);

                var td = new TargetDrive() { Alias = drive.DriveId, Type = drive.DriveType };
                await CheckDrivePayloadsIntegrity(td);
                await VerifyInboxEntiresIntegrity(drive.DriveId, cleanup);

                await VerifyPayloadsFilesInDiskFolder(drive.DriveId, cleanup);
            }
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
        /// Checks the  given header from the database.
        /// Ensures the required payloads and thumbnails are on disk.
        /// Returns null if all are present
        /// Otherwise returns the list of missing payloads / thumbnails as full filename plus directory
        /// If payloads are remote, skips check (caught in VerifyDriveDirectoriesPayloads())
        /// </summary>
        private async Task<List<string>> CheckPayloadsIntegrity(StorageDrive drive, ServerFileHeader header)
        {
            var fileId = header.FileMetadata.File.FileId;
            var payloads = header.FileMetadata?.Payloads ?? [];
            var sl = new List<string>();

            // If the payloads are remote, skip the disk check
            if (header?.FileMetadata?.DataSource?.PayloadsAreRemote == true)
                 return null;
            
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
