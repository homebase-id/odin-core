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
        DriveManager driveManager,
        DriveFileReaderWriter driveFileReaderWriter)
    {
        public async Task VerifyFolder(StorageDrive drive, string folderPath, IDriveFileSystem fs, IOdinContext odinContext)
        {
            var files = GetFilesInDirectory(folderPath, "*.*", 24);

            var fileIds = files
                .Select(f => Path.GetFileNameWithoutExtension(f).Split(DriveFileUtility.PayloadDelimiter)[0])
                .Distinct()
                .Select(f => Guid.TryParse(DriveFileUtility.RestoreFileIdFromDiskString(f).ToString(), out var guid) ? guid : (Guid?)null)
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
                Ordering = QueryBatchOrdering.Default,
                Sorting = QueryBatchType.FileId
            };

            var driveId = await driveManager.GetDriveIdByAliasAsync(targetDrive, true);
            var storageDrive = await driveManager.GetDriveAsync(driveId.GetValueOrDefault());
            var batch = await fs.Query.GetBatch(driveId.GetValueOrDefault(), query, options, odinContext);

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

            if (!CheckFileIntegrity(drive, header))
            {
                // We got an incomplete header with a missing payload / thumbnail - what should we do?
                return false;
            }

            CleanOrphanedPayloadsAndThumbnails(drive, header);

            return true;
        }

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


        public void HardDeleteThumbnailFile(StorageDrive drive, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, int height,
            int width)
        {
            Benchmark.Milliseconds(logger, nameof(HardDeleteThumbnailFile), () =>
            {
                var fileName = GetThumbnailFileName(fileId, width, height, payloadKey, payloadUid);
                var dir = GetFilePath(drive, fileId, FilePart.Thumb);
                var path = Path.Combine(dir, fileName);

                driveFileReaderWriter.DeleteFile(path);
            });
        }

        public void HardDeletePayloadFile(StorageDrive drive, Guid fileId, string payloadKey, string payloadUid)
        {
            Benchmark.Milliseconds(logger, nameof(HardDeletePayloadFile), () =>
            {
                var pathAndFilename = GetPayloadFilePath(drive, fileId, payloadKey, payloadUid);

                //
                // Re-enable DELETION this after we are good with actually deleting the file
                //

                // _driveFileReaderWriter.DeleteFile(path);

                var target = pathAndFilename.Replace(".payload", TenantPathManager.DeletePayloadExtension);
                logger.LogDebug("HardDeletePayloadFile -> attempting to rename [{source}] to [{dest}]",
                    pathAndFilename,
                    target);

                if (driveFileReaderWriter.FileExists(pathAndFilename))
                {
                    driveFileReaderWriter.MoveFile(pathAndFilename, target);
                }
                else
                {
                    logger.LogError("HardDeletePayloadFile -> source payload does not exist [{pathAndFilename}]", pathAndFilename);
                }

                // delete the thumbnails
                // _driveFileReaderWriter.DeleteFilesInDirectory(dir, thumbnailSearchPattern);

                // 1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760-500x500.thumb
                var thumbnailSearchPattern = GetThumbnailSearchMask(fileId, payloadKey, new UnixTimeUtcUnique(long.Parse(payloadUid)));
                var dir = GetPayloadPath(drive, fileId);
                var thumbnailFiles = driveFileReaderWriter.GetFilesInDirectory(dir, thumbnailSearchPattern);
                foreach (var thumbnailFile in thumbnailFiles)
                {
                    var thumbnailTarget = thumbnailFile.Replace(".thumb", TenantPathManager.DeletedThumbExtension);

                    if (driveFileReaderWriter.FileExists(thumbnailFile))
                    {
                        driveFileReaderWriter.MoveFile(thumbnailFile, thumbnailTarget);
                    }
                    else
                    {
                        logger.LogError("HardDeletePayloadFile -> Renaming Thumbnail: source thumbnail does not exist [{thumbnailFile}]",
                            thumbnailFile);
                    }
                }
            });
        }

        public void HardDeleteAllPayloadFiles(StorageDrive drive, Guid fileId)
        {
            Benchmark.Milliseconds(logger, nameof(HardDeleteAllPayloadFiles), () =>
            {
                var fn = TenantPathManager.GuidToPathSafeString(fileId);
                var searchPattern = $"{fn}*";

                // note: no need to delete thumbnails separately due to the aggressive searchPattern
                var dir = GetPayloadPath(drive, fileId);
                driveFileReaderWriter.DeleteFilesInDirectory(dir, searchPattern);
            });
        }

        public bool PayloadExistsOnDisk(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor)
        {
            var path = GetPayloadFilePath(drive, fileId, descriptor);
            var exists = driveFileReaderWriter.FileExists(path);

            if (!exists)
                Console.WriteLine($"File integrity problem with driveId {drive.Id.ToString()} fileId {fileId.ToString()} file {path}");

            return exists;
        }

        public bool ThumbnailExistsOnDisk(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor,
            ThumbnailDescriptor thumbnailDescriptor)
        {
            var path = GetThumbnailPath(drive, fileId, thumbnailDescriptor.PixelWidth,
                thumbnailDescriptor.PixelHeight,
                descriptor.Key,
                descriptor.Uid);

            var exists = driveFileReaderWriter.FileExists(path);

            if (!exists)
                Console.WriteLine($"File integrity problem with driveId {drive.Id.ToString()} fileId {fileId.ToString()} file {path}");

            return exists;
        }

        // /// <summary>
        // /// Checks if the header file exists in db.  Does not check the validity of the header
        // /// </summary>
        // public async Task<bool> HeaderFileExists(StorageDrive drive, Guid fileId, FileSystemType fileSystemType)
        // {
        //     var header = await this.GetServerFileHeader(drive, fileId, fileSystemType);
        //     if (header == null)
        //     {
        //         return false;
        //     }
        //
        //     return true;
        // }

        // /// <summary>
        // /// Removes all traces of a file and deletes its record from the index
        // /// </summary>
        // public async Task HardDeleteAsync(StorageDrive drive, Guid fileId, IDriveFileSystem fs)
        // {
        //     Benchmark.Milliseconds(logger, "HardDeleteAsync", () => { HardDeleteAllPayloadFiles(drive, fileId); });
        //     await fs.Storage.HardDeleteLongTermFile(drive, GetInternalFile(drive, fileId));
        // }

        //
        // public async Task<ServerFileHeader> GetServerFileHeader(StorageDrive drive, Guid fileId, FileSystemType fileSystemType)
        // {
        //     var header = await driveQuery.GetFileHeaderAsync(drive, fileId, fileSystemType);
        //     return header;
        // }

        /// <summary>
        /// Removes any payloads that are not in the provided list
        /// </summary>
        public void HardDeleteOrphanPayloadFiles(StorageDrive drive, Guid fileId, List<PayloadDescriptor> expectedPayloads)
        {
            logger.LogDebug("HardDeleteOrphanPayloadFiles called but we are ignoring");
            //
            // if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
            // {
            //     logger.LogDebug("HardDeleteOrphanPayloadFiles called on feed drive; ignoring since feed does not receive the payloads");
            //     return;
            // }
            //
            // Benchmark.Milliseconds(logger, nameof(HardDeleteOrphanPayloadFiles), () =>
            // {
            //     /*
            //        ├── 1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760.payload
            //        ├── 1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760-20x20.thumb
            //        ├── 1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760-400x400.thumb
            //        ├── 1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760-500x500.thumb
            //      */
            //
            //     var payloadFileDirectory = GetPayloadPath(drive, fileId);
            //     if (!driveFileReaderWriter.DirectoryExists(payloadFileDirectory))
            //     {
            //         return;
            //     }
            //
            //     var searchPattern = GetPayloadSearchMask(fileId);
            //     var files = driveFileReaderWriter.GetFilesInDirectory(payloadFileDirectory, searchPattern);
            //     var orphans = GetOrphanedPayloads(files, expectedPayloads);
            //
            //     foreach (var orphan in orphans)
            //     {
            //         HardDeletePayloadFile(drive, fileId, orphan.Key, orphan.Uid);
            //     }
            //
            //     // Delete all orphaned thumbnails on a payload I am keeping
            //     foreach (var payloadDescriptor in expectedPayloads)
            //     {
            //         HardDeleteOrphanThumbnailFiles(drive, fileId, payloadDescriptor);
            //     }
            // });
        }

        private List<ParsedPayloadFileRecord> GetOrphanedPayloads(string[] files, List<PayloadDescriptor> expectedPayloads)
        {
            // examine all payload files for a given fileId, regardless of key.
            // we'll compare the file below before deleting

            var orphanFiles = new List<ParsedPayloadFileRecord>();

            foreach (var payloadFilePath in files)
            {
                var filename = Path.GetFileNameWithoutExtension(payloadFilePath);
                var fileRecord = TenantPathManager.ParsePayloadFilename(filename);

                bool isKept = expectedPayloads.Any(p => p.Key.Equals(fileRecord.Key, StringComparison.InvariantCultureIgnoreCase) &&
                                                        p.Uid.ToString() == fileRecord.Uid);

                if (!isKept)
                {
                    orphanFiles.Add(fileRecord);
                }
            }

            return orphanFiles;
        }

        private List<ParsedThumbnailFileRecord> GetOrphanThumbnails(StorageDrive drive, Guid fileId, PayloadDescriptor payloadDescriptor)
        {
            // examine all payload files for a given fileId, regardless of key.
            // we'll compare the file below before deleting

            var expectedThumbnails = payloadDescriptor.Thumbnails?.ToList() ?? [];
            var dir = GetFilePath(drive, fileId, FilePart.Thumb);
            if (driveFileReaderWriter.DirectoryExists(dir))
            {
                return [];
            }

            // ├── 1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760-*x*.thumb
            var thumbnailSearchPatternForPayload = GetThumbnailSearchMask(fileId, payloadDescriptor.Key, payloadDescriptor.Uid);
            var thumbnailFilePathsForPayload = driveFileReaderWriter.GetFilesInDirectory(dir, thumbnailSearchPatternForPayload);
            logger.LogDebug("Deleting thumbnails: Found {count} for file({fileId}) with path-pattern ({pattern})",
                thumbnailFilePathsForPayload.Length,
                fileId,
                thumbnailSearchPatternForPayload);

            var orphans = new List<ParsedThumbnailFileRecord>();

            foreach (var thumbnailFilePath in thumbnailFilePathsForPayload)
            {
                var filename = Path.GetFileNameWithoutExtension(thumbnailFilePath);
                var thumbnailFileRecord = TenantPathManager.ParseThumbnailFilename(filename);

                // is the file from the payload and thumbnail size
                var keepThumbnail = payloadDescriptor.Key.Equals(thumbnailFileRecord.Key, StringComparison.InvariantCultureIgnoreCase) &&
                                    payloadDescriptor.Uid.ToString() == thumbnailFileRecord.Uid &&
                                    expectedThumbnails.Exists(thumb => thumb.PixelWidth == thumbnailFileRecord.Width &&
                                                                       thumb.PixelHeight == thumbnailFileRecord.Height);
                if (!keepThumbnail)
                {
                    orphans.Add(thumbnailFileRecord);
                }
            }

            return orphans;
        }

        /// <summary>
        /// Removes all thumbnails on disk which are not in the provided list.
        /// </summary>
        private void HardDeleteOrphanThumbnailFiles(StorageDrive drive, Guid fileId, PayloadDescriptor payloadDescriptor)
        {
            Benchmark.Milliseconds(logger, nameof(HardDeleteOrphanThumbnailFiles), () =>
            {
                var orphanedThumbnailFileRecords = GetOrphanThumbnails(drive, fileId, payloadDescriptor);
                foreach (var orphanThumbnail in orphanedThumbnailFileRecords)
                {
                    HardDeleteThumbnailFile(drive, fileId, payloadDescriptor.Key, payloadDescriptor.Uid,
                        orphanThumbnail.Width, orphanThumbnail.Height);
                }
            });
        }

        private string GetThumbnailFileName(Guid fileId, int width, int height, string payloadKey, UnixTimeUtcUnique payloadUid)
        {
            var extension = DriveFileUtility.GetThumbnailFileExtension(payloadKey, payloadUid, width, height);
            return $"{TenantPathManager.GuidToPathSafeString(fileId)}{DriveFileUtility.FileNameSectionDelimiter}{extension}";
        }

        private string GetThumbnailPath(StorageDrive drive, Guid fileId, int width, int height, string payloadKey,
            UnixTimeUtcUnique payloadUid)
        {
            var thumbnailFileName = GetThumbnailFileName(fileId, width, height, payloadKey, payloadUid);
            var filePath = GetFilePath(drive, fileId, FilePart.Thumb);
            var thumbnailPath = Path.Combine(filePath, thumbnailFileName);
            return thumbnailPath;
        }

        private string GetThumbnailSearchMask(Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid)
        {
            var extension = DriveFileUtility.GetThumbnailFileExtension(payloadKey, payloadUid, "*", "*");
            return $"{TenantPathManager.GuidToPathSafeString(fileId)}{DriveFileUtility.FileNameSectionDelimiter}{extension}";
        }

        private string GetFilePath(StorageDrive drive, Guid fileId, FilePart filePart, bool ensureExists = false)
        {
            var path = filePart is FilePart.Payload or FilePart.Thumb
                ? drive.GetLongTermPayloadStoragePath()
                : throw new OdinSystemException($"Invalid FilePart {filePart}");

            //07e5070f-173b-473b-ff03-ffec2aa1b7b8
            //The positions in the time guid are hex values as follows
            //from new DateTimeOffset(2021, 7, 21, 23, 59, 59, TimeSpan.Zero);
            //07e5=year,07=month,0f=day,17=hour,3b=minute

            var parts = fileId.ToString().Split("-");
            var yearMonthDay = parts[0];
            var year = yearMonthDay.Substring(0, 4);
            var month = yearMonthDay.Substring(4, 2);
            var day = yearMonthDay.Substring(6, 2);
            var hourMinute = parts[1];
            var hour = hourMinute[..2];

            string dir = Path.Combine(path, year, month, day, hour);

            if (ensureExists)
            {
                Benchmark.Milliseconds(logger, "GetFilePath/CreateDirectory", () => driveFileReaderWriter.CreateDirectory(dir));
            }

            return dir;
        }

        private string GetPayloadPath(StorageDrive drive, Guid fileId, bool ensureExists = false)
        {
            return GetFilePath(drive, fileId, FilePart.Payload, ensureExists);
        }

        private string GetPayloadFilePath(StorageDrive drive, Guid fileId, string payloadKey, string payloadUid, bool ensureExists = false)
        {
            var extension = DriveFileUtility.GetPayloadFileExtension(payloadKey, payloadUid);
            var payloadFileName = $"{TenantPathManager.GuidToPathSafeString(fileId)}{DriveFileUtility.FileNameSectionDelimiter}{extension}";
            return Path.Combine(GetPayloadPath(drive, fileId, ensureExists), $"{payloadFileName}");
        }

        private string GetPayloadFilePath(StorageDrive drive, Guid fileId, PayloadDescriptor descriptor, bool ensureExists = false)
        {
            return GetPayloadFilePath(drive, fileId, descriptor.Key, descriptor.Uid.ToString(), ensureExists);
        }

        private string GetPayloadSearchMask(Guid fileId)
        {
            var extension = DriveFileUtility.GetPayloadFileExtension("*", "*");
            var mask = $"{TenantPathManager.GuidToPathSafeString(fileId)}{DriveFileUtility.FileNameSectionDelimiter}{extension}";
            return mask;
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