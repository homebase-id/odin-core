using System;
using System.IO;
using Odin.Core.Time;
using Odin.Services.Base;

namespace Odin.Services.Drives.DriveCore.Storage
{
    // public class TenantPathManager(Guid tenantId, string tenantShard)
    public class TenantPathManager(TenantContext tenantContext)
    {
        public readonly Guid TenantId = tenantContext.DotYouRegistryId;
        public readonly string TenantShard = tenantContext.StorageConfig.PayloadShardKey;

        /*public TenantPathManager(Guid tenantId, string tenantShard, string configRoot, string currentEnvironment)
        {
            this.TenantId = tenantId;
            this.TenantShard = tenantShard;
            PathManager.Init(configRoot, currentEnvironment);
        }*/

        public string GetTenantRootPath()
            => PathManager.GetTenantRootPath(TenantId);

        public string GetHeaderDataStoragePath()
            => PathManager.GetHeaderDataStoragePath(TenantId);

        public string GetTempStorageBasePath()
            => PathManager.GetTempStorageBasePath(TenantId);

        public string GetPayloadStorageBasePath()
            => PathManager.GetPayloadStorageBasePath(TenantId, TenantShard);

        public string GetStaticFileStoragePath(Guid tenantId)
            => PathManager.GetStaticFileStoragePath(TenantId);


        public string GetPayloadFileName(Guid fileId, string key, UnixTimeUtcUnique uid)
         => PathManager.GetPayloadFileName(fileId, key, uid);

        public string GetPayloadDirectory(Guid driveId, Guid fileId, bool ensureExists = false)
            => PathManager.GetPayloadDirectory(TenantId, TenantShard, driveId, fileId, ensureExists);

        public string GetPayloadFilePath(Guid driveId, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, bool ensureExists = false)
            => PathManager.GetPayloadFilePath(TenantId, TenantShard, driveId, fileId, payloadKey, payloadUid, ensureExists);

        public string GetThumbnailDirectory(Guid driveId, Guid fileId, bool ensureExists = false)
            => PathManager.GetThumbnailDirectory(TenantId, TenantShard, driveId, fileId, ensureExists);

        public string GetThumbnailFilePath(Guid driveId, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, int thumbWidth, int thumbHeight)
            => PathManager.GetThumbnailFilePath(TenantId,TenantShard, driveId, fileId, payloadKey, payloadUid, thumbWidth, thumbHeight);

        public string GetThumbnailFileName(Guid fileId, string key, UnixTimeUtcUnique uid, int width, int height)
            => PathManager.GetThumbnailFileName(fileId, key, uid, width, height);
    }


    public static class PathManager
    {
        public static string ConfigRoot;
        public static string CurrentEnvironment;

        // public static readonly string ConfigRoot = Environment.GetEnvironmentVariable("ODIN_CONFIG_PATH") ?? Directory.GetCurrentDirectory();
        // public static readonly string CurrentEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

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
        // Base storage paths
        // ----------------------

        public static string GetTenantRootPath(Guid tenantId)
            => Path.Combine(ConfigRoot, StorageFolder, CurrentEnvironment.ToLowerInvariant(), tenantId.ToString());

        public static string GetHeaderDataStoragePath(Guid tenantId)
            => Path.Combine(GetTenantRootPath(tenantId), HeadersFolder);

        public static string GetTempStorageBasePath(Guid tenantId)
            => Path.Combine(GetTenantRootPath(tenantId), TempFolder);

        public static string GetPayloadStorageBasePath(Guid tenantId, string payloadShardKey)
            => Path.Combine(GetTenantRootPath(tenantId), PayloadsFolder, payloadShardKey);

        public static string GetStaticFileStoragePath(Guid tenantId)
            => Path.Combine(GetTenantRootPath(tenantId), StaticFolder);



        // ----------------------
        // Drive-specific paths
        // ----------------------

        public static string GetDriveTempStoragePath(Guid tenantId, Guid driveId, TempStorageType storageType)
        {
            var driveFolderName = driveId.ToString("N").ToLower();
            var tempBase = Path.Combine(GetTempStorageBasePath(tenantId), DriveFolder, driveFolderName);
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

        public static string GetDriveLongTermPayloadPath(Guid tenantId, string payloadShardKey, Guid driveId)
        {
            var driveFolderName = driveId.ToString("N").ToLower();
            return Path.Combine(GetPayloadStorageBasePath(tenantId, payloadShardKey), DriveFolder, driveFolderName, FilesFolder);
        }

        // ----------------------
        // File-specific paths
        // ----------------------

        public static string GetPayloadFileName(Guid fileId, string key, UnixTimeUtcUnique uid)
        {
            var r = $"{fileId.ToString("N").ToLower()}{FileNameSectionDelimiter}{key.ToLower()}{FileNameSectionDelimiter}{uid.ToString()}{PayloadExtension}";
            return r;
        }

        public static string GetPayloadDirectory(Guid tenantId, string payloadShardKey, Guid driveId, Guid fileId, bool ensureExists = false)
        {
            var r = GetPartitionedFilePath(GetDriveLongTermPayloadPath(tenantId, payloadShardKey, driveId), fileId, ensureExists);
            return r;
        }

        public static string GetPayloadFilePath(Guid tenantId, string payloadShardKey, Guid driveId, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, bool ensureExists = false)
        {
            var fileName = GetPayloadFileName(fileId, payloadKey, payloadUid);
            var dir = GetPayloadDirectory(tenantId, payloadShardKey, driveId, fileId, ensureExists);
            return Path.Combine(dir, fileName);
        }

        public static string GetThumbnailDirectory(Guid tenantId, string payloadShardKey, Guid driveId, Guid fileId, bool ensureExists = false)
            => GetPartitionedFilePath(GetDriveLongTermPayloadPath(tenantId, payloadShardKey, driveId), fileId, ensureExists);

        public static string GetThumbnailFilePath(Guid tenantId, string payloadShardKey, Guid driveId, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, int thumbWidth, int thumbHeight)
        {
            var fileName = GetThumbnailFileName(fileId, payloadKey, payloadUid, thumbWidth, thumbHeight);
            var dir = GetThumbnailDirectory(tenantId, payloadShardKey, driveId, fileId);
            return Path.Combine(dir, fileName);
        }

        public static string GetThumbnailFileName(Guid fileId, string key, UnixTimeUtcUnique uid, int width, int height)
        {
            var size = $"{width}{ThumbnailSizeDelimiter}{height}";
            return $"{fileId.ToString("N").ToLower()}{FileNameSectionDelimiter}{key.ToLower()}{FileNameSectionDelimiter}{uid.ToString()}{FileNameSectionDelimiter}{size}{ThumbnailExtension}";
        }

        public static string GetPayloadSearchMask(Guid fileId)
            => $"{fileId:N.ToLower()}{FileNameSectionDelimiter}*{PayloadExtension}";

        public static string GetThumbnailSearchMask(Guid fileId, string payloadKey, UnixTimeUtcUnique uid)
            => $"{fileId:N.ToLower()}{FileNameSectionDelimiter}{payloadKey.ToLower()}{FileNameSectionDelimiter}{uid.ToString().ToLower()}{FileNameSectionDelimiter}*{ThumbnailExtension}";

        // ----------------------
        // Database paths
        // ----------------------

        public static string GetHeaderDbPath(Guid tenantId)
            => Path.Combine(GetHeaderDataStoragePath(tenantId), "header.db");

        public static string GetIndexDbPath(Guid tenantId)
            => Path.Combine(GetHeaderDataStoragePath(tenantId), "index.db");

        public static string GetDriveTransferDbPath(Guid tenantId)
            => Path.Combine(GetHeaderDataStoragePath(tenantId), "drive_transfer.db");

        // ----------------------
        // Internal helpers
        // ----------------------

        public static string GetPartitionedFilePath(string root, Guid fileId, bool ensureExists)
        {
            var id = fileId.ToString("N").ToLower();
            var year = id.Substring(0, 4);
            var month = id.Substring(4, 2);
            var day = id.Substring(6, 2);
            var hour = id.Substring(8, 2);

            var path = Path.Combine(root, year, month, day, hour);
            if (ensureExists) Directory.CreateDirectory(path);
            return path;
        }
    }
}
