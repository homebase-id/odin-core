using System;
using System.IO;
using System.Text.RegularExpressions;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Configuration;
using Odin.Services.Util;

#nullable enable

namespace Odin.Services.Drives.FileSystem.Base;

public record ParsedPayloadFileRecord
{
    public string Filename { get; set; } = "";
    public string Key { get; init; }  = "";
    public UnixTimeUtcUnique Uid { get; init; }
}

public record ParsedThumbnailFileRecord
{
    public string Filename { get; set; } = "";
    public string Key { get; init; } = "";
    public UnixTimeUtcUnique Uid { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

// public class TenantPathManager(Guid tenantId, string tenantShard)
public class TenantPathManager
{
    public readonly Guid TenantId;

    public readonly string TenantShard;
    public readonly string TempStoragePath;
    public readonly string PayloadStoragePath;
    public readonly string HeaderDataStoragePath;
    public readonly string StaticFileStoragePath;

    public readonly string TenantDataRootPath;

    public const string ValidPayloadKeyRegex = "^[a-z0-9_]{8,10}$";
    public const string FileNameSectionDelimiter = "-";
    public const string PayloadExtension = ".payload";
    public const string ThumbnailExtension = ".thumb";
    public const string ThumbnailSizeDelimiter = "x";
    public const string DriveFolder = "drives";
    public const string StorageFolder = "storage";
    public const string RegistrationsFolder = "registrations";
    public const string HeadersFolder = "headers";
    public const string TempFolder = "temp";
    public const string PayloadsFolder = "payloads";
    public const string StaticFolder = "static";
    public const string UploadFolder = "uploads";
    public const string InboxFolder = "inbox";
    public const string FilesFolder = "files";
    public const string DeletePayloadExtension = ".p-deleted";
    public const string DeletedThumbExtension = ".t-deleted";
    public const string PayloadDelimiter = "-";
    public const string TransitThumbnailKeyDelimiter = "|";

    public TenantPathManager(OdinConfiguration config, string payloadShardKey, Guid tenantId)
    {
        TenantId = tenantId;
        TenantShard = payloadShardKey;
        ArgumentException.ThrowIfNullOrEmpty(nameof(TenantShard));

        TenantDataRootPath = config.Host.TenantDataRootPath;
        ArgumentException.ThrowIfNullOrEmpty(nameof(TenantDataRootPath));

        var registrationRoot = Path.Combine(TenantDataRootPath, RegistrationsFolder);
        var rootPath = Path.Combine(registrationRoot, tenantId.ToString());

        HeaderDataStoragePath = Path.Combine(rootPath, HeadersFolder);
        TempStoragePath  = Path.Combine(rootPath, TempFolder);
        StaticFileStoragePath = Path.Combine(rootPath, StaticFolder);
        PayloadStoragePath = Path.Combine(TenantDataRootPath, PayloadsFolder, TenantShard, TenantId.ToString());
    }

    //
    // File String Ops
    //

    public static string GuidToPathSafeString(Guid fileId)
        => $"{fileId:N}";  // .ToLower() not needed - "N" means lowercase

    public static string GetFilename(Guid fileId, string extension)
    {
        string fileStr = TenantPathManager.GuidToPathSafeString(fileId);
        return string.IsNullOrEmpty(extension) ? fileStr : $"{fileStr}.{extension.ToLower()}";
    }

    // ----------------------
    // Disk root locations
    // ----------------------

    public string GetTenantRootBasePath()
        => Path.Combine(TenantDataRootPath, TenantId.ToString());

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
        var s1 = Path.Combine(TempStoragePath, DriveFolder);
        var driveFolderName = GuidToPathSafeString(driveId);
        return Path.Combine(s1, driveFolderName);
    }

    public string GetDriveLongTermStoragePath(Guid driveId)
    {
        // StorageDrive._longTermPayloadPath + "files" = GetLongTermPayloadStoragePath();
        var s2 = Path.Combine(PayloadStoragePath, DriveFolder);
        var driveFolderName = GuidToPathSafeString(driveId);
        return Path.Combine(s2, driveFolderName, FilesFolder);
    }

    public string GetStorageDriveBasePath(Guid driveId)
    {
        // var r = drive.GetLongTermPayloadStoragePath();

        return GetDriveLongTermStoragePath(driveId);
    }

    // ----------------------
    // Drive-specific paths
    // ----------------------

    public string GetDriveTempStoragePath(Guid driveId, TempStorageType storageType)
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
                throw new OdinSystemException($"Unknown storage type: {storageType}");
        }
    }

    public string GetDriveLongTermPayloadPath(Guid driveId)
    {
        var driveFolderName = GuidToPathSafeString(driveId);
        return Path.Combine(GetPayloadStorageBasePath(), DriveFolder, driveFolderName, FilesFolder);
    }


    // ----------------------
    // Payload-specific paths
    // ----------------------
    public static void AssertValidPayloadKey(string payloadKey)
    {
        if (!IsValidPayloadKey(payloadKey))
        {
            throw new OdinClientException($"Missing payload key.  It must match pattern {ValidPayloadKeyRegex}.",
                OdinClientErrorCode.InvalidPayloadNameOrKey);
        }
    }

    public static bool IsValidPayloadKey(string payloadKey)
    {
        if (string.IsNullOrEmpty(payloadKey?.Trim()))
        {
            return false;
        }

        bool isMatch = Regex.IsMatch(payloadKey, ValidPayloadKeyRegex);
        return isMatch;
    }

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

    private static string CreateBasePayloadFileName(string payloadKey, string uid)
    {
        return $"{payloadKey.ToLower()}{FileNameSectionDelimiter}{uid}";
    }

    public static string CreateBasePayloadFileName(string payloadKey, UnixTimeUtcUnique uid)
    {
        return CreateBasePayloadFileName(payloadKey, uid.ToString());
    }

    public static string CreateBasePayloadSearchMask()
    {
        return CreateBasePayloadFileName("*", "*");
    }


    public static string CreateBasePayloadFileNameAndExtension(string payloadKey, UnixTimeUtcUnique uid)
    {
        return $"{payloadKey.ToLower()}{FileNameSectionDelimiter}{uid.ToString()}{PayloadExtension}";
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

    private static string CreateThumbnailFileNameAndExtension(string payloadKey, string payloadUid, string width, string height)
    {
        var bn = CreateBasePayloadFileName(payloadKey, payloadUid);
        var r = $"{bn}{FileNameSectionDelimiter}{width}x{height}{ThumbnailExtension}";
        return r;
    }


    public static string CreateThumbnailFileNameAndExtension(string payloadKey, UnixTimeUtcUnique payloadUid, int width, int height)
    {
        OdinValidationUtils.AssertIsTrue(width > 0, "Thumbnail width must be > 0");
        OdinValidationUtils.AssertIsTrue(height > 0, "Thumbnail height must be > 0");

        return CreateThumbnailFileNameAndExtension(payloadKey, payloadUid.ToString(), width.ToString(), height.ToString());
    }

    public static string CreateThumbnailFileExtensionStarStar(string payloadKey, UnixTimeUtcUnique payloadUid)
    {
        return CreateThumbnailFileNameAndExtension(payloadKey, payloadUid.ToString(), "*", "*");
    }


    public string GetThumbnailDirectory(Guid  driveId, Guid fileId, bool ensureExists = false)
    {
        return GetPayloadDirectory(driveId, fileId, ensureExists);
    }

    public string GetThumbnailFileName(Guid fileId, string key, UnixTimeUtcUnique uid, int width, int height)
    {
        OdinValidationUtils.AssertIsTrue(width > 0, "Thumbnail width must be > 0");
        OdinValidationUtils.AssertIsTrue(height > 0, "Thumbnail height must be > 0");
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
        return Path.Combine(HeaderDataStoragePath, "identity.db");
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
        var parts = filename.Split(TenantPathManager.PayloadDelimiter);
        return new ParsedPayloadFileRecord()
        {
            Filename = parts[0],
            Key = parts[1],
            Uid = new UnixTimeUtcUnique(long.Parse(parts[2]))
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

        var parts = filename.Split(TenantPathManager.PayloadDelimiter);
        var fileNameOnDisk = parts[0]; // not used
        var payloadKeyOnDisk = parts[1];
        var payloadUidOnDisk = long.Parse(parts[2]);
        var thumbnailSize = parts[3];
        var sizeParts = thumbnailSize.Split(ThumbnailSizeDelimiter);
        var widthOnDisk = int.Parse(sizeParts[0]);
        var heightOnDisk = int.Parse(sizeParts[1]);

        return new ParsedThumbnailFileRecord
        {
            Filename = fileNameOnDisk,
            Key = payloadKeyOnDisk,
            Uid = new UnixTimeUtcUnique(payloadUidOnDisk),
            Width = widthOnDisk,
            Height = heightOnDisk
        };
    }
}