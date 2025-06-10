using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Configuration;
using Odin.Services.Util;

[assembly: InternalsVisibleTo("Odin.Services.Tests")]

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

public class TenantPathManager
{
 
    public static readonly string TransferInstructionSetExtension = "transferkeyheader";
    public const string MetadataExtension = "metadata";
    
    public const string ValidPayloadKeyRegex = "^[a-z0-9_]{8,10}$";
    public const string FileNameSectionDelimiter = "-";
    public const string PayloadExtension = ".payload";
    public const string ThumbnailExtension = ".thumb";
    public const string ThumbnailSizeDelimiter = "x";
    public const string DrivesFolder = "drives";
    public const string SslFolder = "ssl";
    public const string RegistrationsFolder = "registrations";
    public const string HeadersFolder = "headers";
    public const string TempFolder = "temp";
    public const string PayloadsFolder = "payloads";
    public const string StaticFolder = "static";
    public const string UploadFolder = "uploads";
    public const string InboxFolder = "inbox";
    public const string FilesFolder = "files";
    public const string PayloadDelimiter = "-";
    public const string TransitThumbnailKeyDelimiter = "|";
    public const string RegJson = "reg.json";

    public readonly string RootPath; // e.g. /data/tenants
    public readonly string RootRegistrationsPath;  // e.g. /data/tenants/registrations
    public readonly string RootPayloadsPath;  // e.g. /data/tenants/payloads

    public readonly string RegistrationPath;  // e.g. /data/tenants/registrations/<tenant-id>/
    public readonly string HeadersPath;  // e.g. /data/tenants/registrations/<tenant-id>/headers
    public readonly string SslPath; // e.g. /data/tenants/registrations/<tenant-id>/ssl
    public readonly string StaticPath;  // e.g. /data/tenants/registrations/<tenant-id>/static
    public readonly string TempPath;  // e.g. /data/tenants/registrations/<tenant-id>/temp
    public readonly string TempDrivesPath;  // e.g. /data/tenants/registrations/<tenant-id>/temp/drives

    public readonly string PayloadsPath;  // e.g. /data/tenants/payloads/<tenant-id>/
    public readonly string PayloadsDrivesPath;  // e.g. /data/tenants/payloads/<tenant-id>/drives

    public TenantPathManager(OdinConfiguration config, Guid tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Host.TenantDataRootPath, nameof(config.Host.TenantDataRootPath));

        var tenant = tenantId.ToString();

        RootPath = config.Host.TenantDataRootPath;
        RootRegistrationsPath = Path.Combine(RootPath, RegistrationsFolder);
        RootPayloadsPath = Path.Combine(RootPath, PayloadsFolder);

        RegistrationPath = Path.Combine(RootRegistrationsPath, tenant);
        HeadersPath = Path.Combine(RegistrationPath, HeadersFolder);
        TempPath = Path.Combine(RegistrationPath, TempFolder);
        TempDrivesPath = Path.Combine(TempPath, DrivesFolder);
        SslPath = Path.Combine(RegistrationPath, SslFolder);
        StaticPath = Path.Combine(RegistrationPath, StaticFolder);

        PayloadsPath = Path.Combine(RootPayloadsPath, tenant);
        PayloadsDrivesPath = Path.Combine(PayloadsPath, DrivesFolder);
    }

    //
    // File String Ops
    //

    public static string GuidToPathSafeString(Guid fileId)
        => $"{fileId:N}";  // .ToLower() not needed - "N" means lowercase

    public static string GetFilename(Guid fileId, string extension)
    {
        var fileStr = GuidToPathSafeString(fileId);
        return string.IsNullOrEmpty(extension) ? fileStr : $"{fileStr}.{extension.ToLower()}";
    }

    // ----------------------
    // Disk root locations
    // ----------------------

    // e.g. /data/tenants/registrations/<tenant-id>/temp/drives/<drive-id>/inbox
    public string GetDriveInboxPath(Guid driveId)
    {
        return Path.Combine(TempDrivesPath, GuidToPathSafeString(driveId), InboxFolder);
    }

    // e.g. /data/tenants/registrations/<tenant-id>/temp/drives/<drive-id>/uploads
    public string GetDriveUploadPath(Guid driveId)
    {
        return Path.Combine(TempDrivesPath, GuidToPathSafeString(driveId), UploadFolder);
    }

    // e.g. /data/tenants/payloads/<tenant-id>/drives/<drive-id>/files
    public string GetDrivePayloadPath(Guid driveId)
    {
        return Path.Combine(PayloadsDrivesPath, GuidToPathSafeString(driveId), FilesFolder);
    }

    // ----------------------
    // Payload-specific paths
    // ----------------------
    public static void AssertValidPayloadKey(string payloadKey)
    {
        if (!IsValidPayloadKey(payloadKey))
        {
            // SEB:TODO this should be a validation exception, mapped to a 400 in upper layers
            throw new OdinClientException($"Missing payload key.  It must match pattern {ValidPayloadKeyRegex}.",
                OdinClientErrorCode.InvalidPayloadNameOrKey);
        }
    }

    public static bool IsValidPayloadKey(string payloadKey)
    {
        if (string.IsNullOrWhiteSpace(payloadKey))
        {
            return false;
        }

        var isMatch = Regex.IsMatch(payloadKey, ValidPayloadKeyRegex);
        return isMatch;
    }

    internal static string GetPayloadDirectoryFromGuid(Guid fileId)
    {
        var (highNibble, lowNibble) = GuidHelper.GetLastTwoNibbles(fileId);
        return Path.Combine(highNibble.ToString(), lowNibble.ToString());
    }

    public string GetPayloadDirectory(Guid driveId, Guid fileId, bool ensureExists = false)
    {
        var root = GetDrivePayloadPath(driveId);
        var subdir = GetPayloadDirectoryFromGuid(fileId);
        var path = Path.Combine(root, subdir);
        if (ensureExists) Directory.CreateDirectory(path);
        return path;
    }

    private static string GetBasePayloadFileName(string payloadKey, string uid)
    {
        return $"{payloadKey.ToLower()}{FileNameSectionDelimiter}{uid}";
    }

    private static string GetBasePayloadFileName(string payloadKey, UnixTimeUtcUnique uid)
    {
        return GetBasePayloadFileName(payloadKey, uid.ToString());
    }


    public static string GetBasePayloadFileNameAndExtension(string payloadKey, UnixTimeUtcUnique uid)
    {
        return $"{GetBasePayloadFileName(payloadKey, uid)}{PayloadExtension}";
    }


    internal static string GetPayloadFileName(Guid fileId, string key, UnixTimeUtcUnique uid)
    {
        return $"{GuidToPathSafeString(fileId)}{FileNameSectionDelimiter}{GetBasePayloadFileNameAndExtension(key, uid)}";
    }

    public string GetPayloadDirectoryAndFileName(Guid driveId, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, bool ensureExists = false)
    {
        var fileName = GetPayloadFileName(fileId, payloadKey, payloadUid);
        var dir = GetPayloadDirectory(driveId, fileId, ensureExists);
        return Path.Combine(dir, fileName);
    }

    public static string GetBasePayloadSearchMask()
    {
        return GetBasePayloadFileName("*", "*");
    }

    // ----------------------
    // Thumbnail-specific paths
    // ----------------------

    private static string GetThumbnailFileNameAndExtension(string payloadKey, string payloadUid, string width, string height)
    {
        var bn = GetBasePayloadFileName(payloadKey, payloadUid);
        var r = $"{bn}{FileNameSectionDelimiter}{width}x{height}{ThumbnailExtension}";
        return r;
    }

    public static string GetThumbnailFileNameAndExtension(string payloadKey, UnixTimeUtcUnique payloadUid, int width, int height)
    {
        OdinValidationUtils.AssertIsTrue(width > 0, "Thumbnail width must be > 0");
        OdinValidationUtils.AssertIsTrue(height > 0, "Thumbnail height must be > 0");

        return GetThumbnailFileNameAndExtension(payloadKey, payloadUid.ToString(), width.ToString(), height.ToString());
    }


    internal string GetThumbnailDirectory(Guid driveId, Guid fileId, bool ensureExists = false)
    {
        return GetPayloadDirectory(driveId, fileId, ensureExists);
    }

    public static string GetThumbnailFileName(Guid fileId, string key, UnixTimeUtcUnique uid, int width, int height)
    {
        OdinValidationUtils.AssertIsTrue(width > 0, "Thumbnail width must be > 0");
        OdinValidationUtils.AssertIsTrue(height > 0, "Thumbnail height must be > 0");
        var size = $"{width}{ThumbnailSizeDelimiter}{height}";
        return $"{GuidToPathSafeString(fileId)}{FileNameSectionDelimiter}{key.ToLower()}{FileNameSectionDelimiter}{uid.ToString()}{FileNameSectionDelimiter}{size}{ThumbnailExtension}";
    }

    public string GetThumbnailDirectoryAndFileName(Guid driveId, Guid fileId, string payloadKey, UnixTimeUtcUnique payloadUid, int thumbWidth, int thumbHeight)
    {
        var fileName = GetThumbnailFileName(fileId, payloadKey, payloadUid, thumbWidth, thumbHeight);
        var dir = GetThumbnailDirectory(driveId, fileId, false);
        return Path.Combine(dir, fileName);
    }

    public static string GetThumbnailFileExtensionStarStar(string payloadKey, UnixTimeUtcUnique payloadUid)
    {
        return GetThumbnailFileNameAndExtension(payloadKey, payloadUid.ToString(), "*", "*");
    }

    // ----------------------
    // Directories
    // ----------------------

    public void CreateDirectories()
    {
        Directory.CreateDirectory(HeadersPath);
        Directory.CreateDirectory(TempPath);
        Directory.CreateDirectory(PayloadsPath);
        Directory.CreateDirectory(StaticPath);
    }

    public void CreateSslRootDirectory()
    {
        Directory.CreateDirectory(SslPath);
    }

    // ----------------------
    // Database paths
    // ----------------------

    public string GetIdentityDatabasePath()
    {
        return Path.Combine(HeadersPath, "identity.db");
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