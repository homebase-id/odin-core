using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Odin.Core.Time;
using Odin.Core.Trie;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Tenant;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public record ParsedPayloadFileRecord
    {
        public string Filename { get; set; }
        public string Key { get; init; }
        public string Uid { get; init; }
    }

    public record ParsedThumbnailFileRecord
    {
        public string Filename { get; set; }
        public string Key { get; init; }
        public string Uid { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }

    // public class TenantPathManager(Guid tenantId, string tenantShard)
    public class TenantPathManager(TenantContext tenantContext)
    {
        private readonly TenantContext _tenantContext = tenantContext;
        public readonly Guid TenantId = tenantContext.DotYouRegistryId;
        public readonly string TenantShard = tenantContext.StorageConfig.PayloadShardKey;

        public static string ConfigRoot = Environment.GetEnvironmentVariable("ODIN_CONFIG_PATH") ?? Directory.GetCurrentDirectory();
        public static string CurrentEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        public static readonly string FileNameSectionDelimiter = "-";
        public static readonly string PayloadExtension = ".payload";
        public static readonly string ThumbnailExtension = ".thumb";
        public static readonly string ThumbnailSizeDelimiter = "x";
        public static readonly string DriveFolder = "drives";
        public static readonly string StorageFolder = "storage";
        public static readonly string HeadersFolder = "headers";
        public static readonly string TempFolder = "temp";
        public static readonly string PayloadsFolder = "payloads";
        public static readonly string StaticFolder = "static";
        public static readonly string UploadFolder = "uploads";
        public static readonly string InboxFolder = "inbox";
        public static readonly string FilesFolder = "files";


        //
        // File String Ops
        //

        public static string GuidToPathSafeString(Guid fileId)
            => $"{fileId.ToString("N")}";  // .ToLower() not needed - "N" means lowercase

        // ----------------------
        // Disk root locations
        // ----------------------

        public string GetTenantRootBasePath()
            => Path.Combine(ConfigRoot, StorageFolder, CurrentEnvironment.ToLowerInvariant(), TenantId.ToString());

        public string GetHeaderDataStorageBasePath()
            => Path.Combine(GetTenantRootBasePath(), HeadersFolder);

        public string GetTempStorageBasePath()
            => Path.Combine(GetTenantRootBasePath(), TempFolder);

        public string GetPayloadStorageBasePath()
            => Path.Combine(GetTenantRootBasePath(), PayloadsFolder, TenantShard);

        public string GetStaticFileStorageBasePath()
            => Path.Combine(GetTenantRootBasePath(), StaticFolder);

        public string GetDriveTempStoragePath(Guid driveId)
        {
            // StorageDrive._tempDataRootPath
            var s1 = Path.Combine(_tenantContext.StorageConfig.TempStoragePath, DriveFolder);
            var _driveFolderName = GuidToPathSafeString(driveId);
            return Path.Combine(s1, _driveFolderName);
        }

        public string GetDriveLongTermStoragePath(Guid driveId)
        {
            // StorageDrive._longTermPayloadPath + "files" = GetLongTermPayloadStoragePath();
            var s2 = Path.Combine(_tenantContext.StorageConfig.PayloadStoragePath, DriveFolder);
            var _driveFolderName = GuidToPathSafeString(driveId);
            return Path.Combine(s2, _driveFolderName, FilesFolder);
        }

        public string GetStorageDriveBasePath(Guid driveId)
        {
            // var r = drive.GetLongTermPayloadStoragePath();

            return GetDriveLongTermStoragePath(driveId);
        }

        // ----------------------
        // Drive-specific paths
        // ----------------------

        public string GetDriveTempStoragePath(Guid tenantId, Guid driveId, TempStorageType storageType)
        {
            var driveFolderName = GuidToPathSafeString(driveId);
            var tempBase = Path.Combine(GetTempStorageBasePath(), DriveFolder, driveFolderName);
            switch (storageType)
            {
                case TempStorageType.Upload:
                    return Path.Combine(tempBase, UploadFolder);
                case TempStorageType.Inbox:
                    return Path.Combine(tempBase, InboxFolder);
                default:
                    throw new Exception($"Unknown storage type: {storageType}");
            }
        }

        public string GetDriveLongTermPayloadPath(Guid tenantId, string payloadShardKey, Guid driveId)
        {
            var driveFolderName = GuidToPathSafeString(driveId);
            return Path.Combine(GetPayloadStorageBasePath(), DriveFolder, driveFolderName, FilesFolder);
        }


        // ----------------------
        // Payload-specific paths
        // ----------------------
        public static string GetPayloadDirectoryFromGuid(Guid fileId)
        {
            var id = GuidToPathSafeString(fileId);
            var year = id.Substring(0, 4);
            var month = id.Substring(4, 2);
            var day = id.Substring(6, 2);
            var hour = id.Substring(8, 2);

            var path = Path.Combine(year, month, day, hour);

            return path;
        }

        public string GetPayloadDirectory(Guid  driveId, Guid fileId, bool ensureExists = false)
        {
            var root = GetStorageDriveBasePath(driveId);
            var subdir = GetPayloadDirectoryFromGuid(fileId);
            var path = Path.Combine(root, subdir);
            if (ensureExists) Directory.CreateDirectory(path);
            return path;
        }

        public static string CreateBasePayloadFileName(string payloadKey, UnixTimeUtcUnique uid)
        {
            return $"{payloadKey.ToLower()}{FileNameSectionDelimiter}{uid.ToString()}";
        }

        public string GetPayloadFileName(Guid fileId, string key, UnixTimeUtcUnique uid)
        {
            return $"{GuidToPathSafeString(fileId)}{FileNameSectionDelimiter}{CreateBasePayloadFileName(key, uid)}{PayloadExtension}";
        }

        public string GetPayloadDirectoryAndFileName(Guid  driveId, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, bool ensureExists = false)
        {
            var fileName = GetPayloadFileName(fileId, payloadKey, payloadUid);
            var dir = GetPayloadDirectory(driveId, fileId, ensureExists);
            return Path.Combine(dir, fileName);
        }

        // ----------------------
        // Thumbnail-specific paths
        // ----------------------
        public string GetThumbnailDirectory(Guid  driveId, Guid fileId, bool ensureExists = false)
        {
            return GetPayloadDirectory(driveId, fileId, ensureExists);
        }

        public string GetThumbnailFileName(Guid fileId, string key, UnixTimeUtcUnique uid, int width, int height)
        {
            var size = $"{width}{ThumbnailSizeDelimiter}{height}";
            return $"{GuidToPathSafeString(fileId)}{FileNameSectionDelimiter}{key.ToLower()}{FileNameSectionDelimiter}{uid.ToString()}{FileNameSectionDelimiter}{size}{ThumbnailExtension}";
        }

        public string GetThumbnailDirectoryandFileName(Guid  driveId, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, int thumbWidth, int thumbHeight)
        {
            var fileName = GetThumbnailFileName(fileId, payloadKey, payloadUid, thumbWidth, thumbHeight);
            var dir = GetThumbnailDirectory(driveId, fileId, false);
            return Path.Combine(dir, fileName);
        }


        // ----------------------
        // Database paths
        // ----------------------

        public string GetHeaderDbPath()
            => Path.Combine(GetHeaderDataStorageBasePath(), "header.db");

        public string GetIndexDbPath()
            => Path.Combine(GetHeaderDataStorageBasePath(), "index.db");

        public string GetDriveTransferDbPath()
            => Path.Combine(GetHeaderDataStorageBasePath(), "drive_transfer.db");

        public string GetIdentityDatabasePath()
        {
            return Path.Combine(_tenantContext.StorageConfig.HeaderDataStoragePath, "identity.db");
        }

        //
        // Parsing
        //
        public static ParsedPayloadFileRecord ParsePayloadFilename(string filename)
        {
            // file name on disk: 1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760.payload
            // fileId is 1fedce18c0022900efbb396f9796d3d0
            // payload key is prfl_pic
            // payload UID is 113599297775861760
            var parts = filename.Split(DriveFileUtility.PayloadDelimiter);
            return new ParsedPayloadFileRecord()
            {
                Filename = parts[0],
                Key = parts[1],
                Uid = parts[2]
            };
        }

        public static ParsedThumbnailFileRecord ParseThumbnailFilename(string filename)
        {
            // filename = "1fedce18c0022900efbb396f9796d3d0-prfl_pic-113599297775861760-400x400.thumb"
            // fileId is 1fedce18c0022900efbb396f9796d3d0
            // payload key is prfl_pic
            // payload UID is 113599297775861760
            // width = 400
            // height 400

            var parts = filename.Split(DriveFileUtility.PayloadDelimiter);
            var fileNameOnDisk = parts[0]; // not used 
            var payloadKeyOnDisk = parts[1];
            var payloadUidOnDisk = parts[2];
            var thumbnailSize = parts[3];
            var sizeParts = thumbnailSize.Split(ThumbnailSizeDelimiter);
            var widthOnDisk = int.Parse(sizeParts[0]);
            var heightOnDisk = int.Parse(sizeParts[1]);

            return new ParsedThumbnailFileRecord
            {
                Filename = fileNameOnDisk,
                Key = payloadKeyOnDisk,
                Uid = payloadUidOnDisk,
                Width = widthOnDisk,
                Height = heightOnDisk
            };
        }

    }
}
