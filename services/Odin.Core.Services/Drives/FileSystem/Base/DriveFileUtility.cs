using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using NodaTime;
using NodaTime.Text;
using Odin.Core.Exceptions;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Time;

namespace Odin.Core.Services.Drives.FileSystem.Base;

public static class DriveFileUtility
{
    public const string ValidPayloadKeyRegex = @"^[a-z0-9_]{8,10}$";
    public const string PayloadDelimiter = "-";
    public const string PayloadExtensionSpecifier = PayloadDelimiter + "{0}.payload";
    public const string TransitThumbnailKeyDelimiter = "|";

    /// <summary>
    /// Converts the ServerFileHeader to a SharedSecretEncryptedHeader
    /// </summary>
    public static SharedSecretEncryptedFileHeader ConvertToSharedSecretEncryptedClientFileHeader(ServerFileHeader header, OdinContextAccessor contextAccessor,
        bool forceIncludeServerMetadata = false)
    {
        if (header == null)
        {
            return null;
        }

        EncryptedKeyHeader sharedSecretEncryptedKeyHeader;
        if (header.FileMetadata.IsEncrypted)
        {
            var storageKey = contextAccessor.GetCurrent().PermissionsContext.GetDriveStorageKey(header.FileMetadata.File.DriveId);
            var keyHeader = header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
            var clientSharedSecret = contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
            sharedSecretEncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, header.EncryptedKeyHeader.Iv, ref clientSharedSecret);
        }
        else
        {
            sharedSecretEncryptedKeyHeader = EncryptedKeyHeader.Empty();
        }

        int priority = 1000;

        //TODO: this a strange place to calculate priority yet it was the best place w/o having to send back the acl outside of this method
        switch (header.ServerMetadata.AccessControlList.RequiredSecurityGroup)
        {
            case SecurityGroupType.Anonymous:
                priority = 500;
                break;
            case SecurityGroupType.Authenticated:
                priority = 400;
                break;
            case SecurityGroupType.Connected:
                priority = 300;
                break;
            case SecurityGroupType.Owner:
                priority = 1;
                break;
        }


        var clientFileHeader = new SharedSecretEncryptedFileHeader()
        {
            FileId = header.FileMetadata.File.FileId,
            TargetDrive = contextAccessor.GetCurrent().PermissionsContext.GetTargetDrive(header.FileMetadata.File.DriveId),
            FileState = header.FileMetadata.FileState,
            FileSystemType = header.ServerMetadata.FileSystemType,
            SharedSecretEncryptedKeyHeader = sharedSecretEncryptedKeyHeader,
            FileMetadata = RedactFileMetadata(header.FileMetadata),
            Priority = priority,
            FileByteCount = header.ServerMetadata.FileByteCount,
        };

        //add additional info
        if (contextAccessor.GetCurrent().Caller.IsOwner || forceIncludeServerMetadata)
        {
            clientFileHeader.ServerMetadata = header.ServerMetadata;
        }

        return clientFileHeader;
    }

    /// <summary>
    /// Gets the <see cref="EncryptedKeyHeader"/> for the payload key using the payload's IV
    /// </summary>
    public static EncryptedKeyHeader GetPayloadEncryptedKeyHeader(
        ServerFileHeader header,
        PayloadDescriptor payloadDescriptor,
        OdinContextAccessor contextAccessor)
    {
        if (header == null)
        {
            return null;
        }

        if (header.FileMetadata.IsEncrypted)
        {
            var storageKey = contextAccessor.GetCurrent().PermissionsContext.GetDriveStorageKey(header.FileMetadata.File.DriveId);
            var keyHeader = header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
            // The design is such that the client uses a different iv for each payload but the same aesKey;
            keyHeader.Iv = payloadDescriptor.Iv;
            var clientSharedSecret = contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
            return EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, keyHeader.Iv, ref clientSharedSecret);
        }

        return EncryptedKeyHeader.Empty();
    }

    private static ClientFileMetadata RedactFileMetadata(FileMetadata fileMetadata)
    {
        var clientFile = new ClientFileMetadata
        {
            Created = fileMetadata.Created,
            Updated = fileMetadata.Updated,
            AppData = fileMetadata.AppData,
            GlobalTransitId = fileMetadata.GlobalTransitId,
            IsEncrypted = fileMetadata.IsEncrypted,
            SenderOdinId = fileMetadata.SenderOdinId,
            ReferencedFile = fileMetadata.ReferencedFile,
            ReactionPreview = fileMetadata.ReactionPreview,
            Payloads = fileMetadata.Payloads,
            VersionTag = fileMetadata.VersionTag.GetValueOrDefault()
        };
        return clientFile;
    }

    public static ThumbnailDescriptor FindMatchingThumbnail(List<ThumbnailDescriptor> thumbs, int width, int height, bool directMatchOnly)
    {
        if (null == thumbs || !thumbs.Any())
        {
            return null;
        }

        var directMatchingThumb = thumbs.SingleOrDefault(t => t.PixelHeight == height && t.PixelWidth == width);
        if (null != directMatchingThumb)
        {
            return directMatchingThumb;
        }

        if (directMatchOnly)
        {
            return null;
        }

        //TODO: add more logic here to compare width and height separately or together
        var nextSizeUp = thumbs.FirstOrDefault(t => t.PixelHeight > height || t.PixelWidth > width);
        if (null == nextSizeUp)
        {
            nextSizeUp = thumbs.LastOrDefault();
            if (null == nextSizeUp)
            {
                return null;
            }
        }

        return nextSizeUp;
    }

    public static string GetPayloadFileExtension(string key)
    {
        AssertValidPayloadKey(key);
        // string extenstion = $"-{key.ToLower()}.{FilePart.Payload.ToString().ToLower()}";
        string extenstion = string.Format(PayloadExtensionSpecifier, key.ToLower());
        return extenstion;
    }

    public static bool TryParseLastModifiedHeader(HttpContentHeaders headers, out UnixTimeUtc? lastModified)
    {
        if (headers?.TryGetValues(HttpHeaderConstants.LastModified, out var values) ?? false)
        {
            var lastModifiedValue = values.FirstOrDefault();

            const string dateTimePattern = "ddd, dd MMM yyyy HH:mm:ss 'GMT'";
            if (DateTimeOffset.TryParseExact(lastModifiedValue, dateTimePattern, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result))
            {
                Instant instant = Instant.FromDateTimeOffset(result);
                lastModified = new UnixTimeUtc(instant);
                return true;
            }

            // if (lastModifiedValue != null && DateTime.TryParse(lastModifiedValue, out var lastModifiedDateTime))
            // {
            //     InstantPattern pattern = InstantPattern.ExtendedIso;
            //     ParseResult<Instant> parseResult = pattern.Parse(lastModifiedValue);
            //     if (parseResult.Success)
            //     {
            //         Instant instant = parseResult.Value;
            //         lastModified = new UnixTimeUtc(instant);
            //         return true;
            //     }
            // }
        }

        lastModified = null;
        return false;
    }

    public static string GetLastModifiedHeaderValue(UnixTimeUtc? lastModified)
    {
        var instant = (Instant)lastModified.GetValueOrDefault();
        return instant.ToDateTimeUtc().ToString("R");
    }

    public static void AssertValidPayloadKey(string payloadKey)
    {
        if (null == payloadKey)
        {
            throw new OdinClientException($"Missing payload key.  It must match pattern {ValidPayloadKeyRegex}.",
                OdinClientErrorCode.InvalidPayloadNameOrKey);
        }

        bool isMatch = Regex.IsMatch(payloadKey, ValidPayloadKeyRegex);
        if (!isMatch)
        {
            throw new OdinClientException($"Invalid payload key {payloadKey}.  It must match pattern {ValidPayloadKeyRegex}.",
                OdinClientErrorCode.InvalidPayloadNameOrKey);
        }
    }

    public static string GetThumbnailFileExtension(int width, int height, string payloadKey)
    {
        if (string.IsNullOrEmpty(payloadKey?.Trim()))
        {
            throw new OdinClientException($"PayloadKey is null or empty for the thumbnail with width:{width} x height:{height}.",
                OdinClientErrorCode.InvalidPayloadNameOrKey);
        }

        //TODO: move this down into the long term storage manager
        string extenstion = $"-{width}x{height}-{payloadKey}.thumb";
        return extenstion.ToLower();
    }

    public static void AssertVersionTagMatch(Guid? currentVersionTag, Guid? versionTagToCompare)
    {
        if (currentVersionTag != versionTagToCompare)
        {
            throw new OdinClientException($"Invalid version tag {versionTagToCompare}", OdinClientErrorCode.VersionTagMismatch);
        }
    }
}