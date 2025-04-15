using System;
using System.Diagnostics;
using System.IO;
using Odin.Core.Time;
using Odin.Core.Trie;
using Odin.Services.Base;
using Odin.Services.Tenant;

namespace Odin.Services.Drives.DriveCore.Storage
{
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
            var _driveFolderName = driveId.ToString("N").ToLower();
            return Path.Combine(s1, _driveFolderName);
        }

        public string GetDriveLongTermStoragePath(Guid driveId)
        {
            // StorageDrive._longTermPayloadPath + "files" = GetLongTermPayloadStoragePath();
            var s2 = Path.Combine(_tenantContext.StorageConfig.PayloadStoragePath, DriveFolder);
            var _driveFolderName = driveId.ToString("N").ToLower();
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
            var driveFolderName = driveId.ToString("N").ToLower();
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
            var driveFolderName = driveId.ToString("N").ToLower();
            return Path.Combine(GetPayloadStorageBasePath(), DriveFolder, driveFolderName, FilesFolder);
        }


        // ----------------------
        // Payload-specific paths
        // ----------------------
        public string GetPayloadDirectory(Guid  driveId, Guid fileId, bool ensureExists = false)
        {
            var root = GetStorageDriveBasePath(driveId);

            var id = fileId.ToString("N").ToLower();
            var year = id.Substring(0, 4);
            var month = id.Substring(4, 2);
            var day = id.Substring(6, 2);
            var hour = id.Substring(8, 2);

            var path = Path.Combine(root, year, month, day, hour);
            if (ensureExists) Directory.CreateDirectory(path);
            return path;
        }

        public string GetPayloadFileName(Guid fileId, string key, UnixTimeUtcUnique uid)
        {
            var r = $"{fileId.ToString("N").ToLower()}{FileNameSectionDelimiter}{key.ToLower()}{FileNameSectionDelimiter}{uid.ToString()}{PayloadExtension}";
            return r;
        }

        public string GetPayloadFilePath(Guid  driveId, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, bool ensureExists = false)
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
            // return PathManager.GetThumbnailDirectory(TenantId, TenantShard, drive.Id, fileId, ensureExists);
            // public static string GetThumbnailDirectory(Guid tenantId, string payloadShardKey, Guid driveId, Guid fileId, bool ensureExists = false)
            return GetPayloadDirectory(driveId, fileId, ensureExists);
        }

        public string GetThumbnailFileName(Guid fileId, string key, UnixTimeUtcUnique uid, int width, int height)
        {
            var size = $"{width}{ThumbnailSizeDelimiter}{height}";
            return $"{fileId.ToString("N").ToLower()}{FileNameSectionDelimiter}{key.ToLower()}{FileNameSectionDelimiter}{uid.ToString()}{FileNameSectionDelimiter}{size}{ThumbnailExtension}";
        }

        public string GetThumbnailFilePath(Guid  driveId, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, int thumbWidth, int thumbHeight)
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
    }


    public static class PathManager
    {
        // public static string ConfigRoot;
        // public static string CurrentEnvironment;

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

        private static bool _initialized;

        public static void Init(string configRoot, string currentEnvironment)
        {
            if (_initialized) throw new InvalidOperationException("PathManager already initialized.");
            ConfigRoot = configRoot ?? throw new ArgumentNullException(nameof(configRoot));
            CurrentEnvironment = currentEnvironment ?? throw new ArgumentNullException(nameof(currentEnvironment));
            _initialized = true;
        }



        // ----------------------
        // File-specific paths
        // ----------------------



    }
}
