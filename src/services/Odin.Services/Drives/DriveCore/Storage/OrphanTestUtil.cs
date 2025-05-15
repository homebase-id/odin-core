using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.DriveCore.Storage;

public class OrphanTestUtil(
    ILogger<OrphanTestUtil> logger,
    DriveFileReaderWriter driveFileReaderWriter,
    DriveManager driveManager,
    TenantContext tenantContext
    )
{
    private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

    public async Task<bool> HasOrphanPayloadsOrThumbnails(InternalDriveFileId file, List<PayloadDescriptor> expectedPayloads)
    {
        var drive = await driveManager.GetDriveAsync(file.DriveId);
        var payloadFileDirectory = _tenantPathManager.GetPayloadDirectory(drive.Id, file.FileId);

        var searchPattern = GetPayloadSearchMask(file.FileId);
        var files = GetFilesInDirectory(payloadFileDirectory, searchPattern);
        var orphans = GetOrphanedPayloads(files, expectedPayloads);

        if (orphans.Any())
        {
            return true;
        }

        foreach (var descriptor in expectedPayloads)
        {
            var thumbnailOrphans = GetOrphanThumbnails(drive, file.FileId, descriptor);
            if (thumbnailOrphans.Any())
            {
                return true;
            }
        }

        return false;
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
                                                    p.Uid.uniqueTime == fileRecord.Uid.uniqueTime);

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
        var dir = _tenantPathManager.GetPayloadDirectory(drive.Id, fileId);
        if (driveFileReaderWriter.DirectoryExists(dir))
        {
            return [];
        }

        // ├── 1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760-*x*.thumb
        var thumbnailSearchPatternForPayload = GetThumbnailSearchMask(fileId, payloadDescriptor.Key, payloadDescriptor.Uid);
        var thumbnailFilePathsForPayload = GetFilesInDirectory(dir, thumbnailSearchPatternForPayload);
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
                                payloadDescriptor.Uid.uniqueTime == thumbnailFileRecord.Uid.uniqueTime &&
                                expectedThumbnails.Exists(thumb => thumb.PixelWidth == thumbnailFileRecord.Width &&
                                                                   thumb.PixelHeight == thumbnailFileRecord.Height);
            if (!keepThumbnail)
            {
                orphans.Add(thumbnailFileRecord);
            }
        }

        return orphans;
    }
    
    private string GetThumbnailSearchMask(Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid)
    {
        var extension = TenantPathManager.GetThumbnailFileExtensionStarStar(payloadKey, payloadUid);
        return $"{TenantPathManager.GuidToPathSafeString(fileId)}{TenantPathManager.FileNameSectionDelimiter}{extension}";
    }

    private string GetPayloadSearchMask(Guid fileId)
    {
        var extension = DriveFileUtility.GetPayloadFileExtensionStarStar();
        var mask = $"{TenantPathManager.GuidToPathSafeString(fileId)}{TenantPathManager.FileNameSectionDelimiter}{extension}";
        return mask;
    }
    
    private string[] GetFilesInDirectory(string dir, string searchPattern = "*")
    {
        return Directory.GetFiles(dir!, searchPattern);
    }
}