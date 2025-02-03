using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using NodaTime;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Mediator;
using Odin.Services.Peer.Encryption;
using Odin.Services.Util;

namespace Odin.Services.Drives.FileSystem.Base;

public static class DriveFileUtility
{
    public const string ValidPayloadKeyRegex = @"^[a-z0-9_]{8,10}$";
    public const string PayloadDelimiter = "-";
    public const string FileNameSectionDelimiter = "-";
    public const string PayloadExtensionSpecifier = PayloadDelimiter + "{0}.payload";
    public const string TransitThumbnailKeyDelimiter = "|";

    public const int MaxAppDataContentLength = 10 * 1024;
    public const int MaxTinyThumbLength = 1 * 1024;

    /// <summary>
    /// Converts the ServerFileHeader to a SharedSecretEncryptedHeader
    /// </summary>
    public static SharedSecretEncryptedFileHeader CreateClientFileHeader(ServerFileHeader header, IOdinContext odinContext,
        bool forceIncludeServerMetadata = false)
    {
        if (header == null)
        {
            return null;
        }

        EncryptedKeyHeader sharedSecretEncryptedKeyHeader;
        if (header.FileMetadata.IsEncrypted)
        {
            var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(header.FileMetadata.File.DriveId);
            var keyHeader = header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
            var clientSharedSecret = odinContext.PermissionsContext.SharedSecretKey;
            sharedSecretEncryptedKeyHeader =
                EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, header.EncryptedKeyHeader.Iv, ref clientSharedSecret);
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
            TargetDrive = odinContext.PermissionsContext.GetTargetDrive(header.FileMetadata.File.DriveId),
            FileState = header.FileMetadata.FileState,
            FileSystemType = header.ServerMetadata.FileSystemType,
            SharedSecretEncryptedKeyHeader = sharedSecretEncryptedKeyHeader,
            FileMetadata = RedactFileMetadata(header.FileMetadata),
            Priority = priority,
            FileByteCount = header.ServerMetadata.FileByteCount,
        };

        //add additional info
        if (odinContext.Caller.IsOwner || forceIncludeServerMetadata)
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
        IOdinContext odinContext)
    {
        if (header == null)
        {
            return null;
        }

        if (header.FileMetadata.IsEncrypted)
        {
            var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(header.FileMetadata.File.DriveId);
            var keyHeader = header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
            // The design is such that the client uses a different iv for each payload but the same aesKey;

            if (payloadDescriptor.Iv == null)
            {
                throw new OdinSystemException("payload descriptor is missing IV (initialization vector)");
            }

            //TODO: consider falling back

            keyHeader.Iv = payloadDescriptor.Iv;
            var clientSharedSecret = odinContext.PermissionsContext.SharedSecretKey;
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

            TransitCreated = fileMetadata.TransitCreated,
            TransitUpdated = fileMetadata.TransitUpdated,

            AppData = fileMetadata.AppData,
            LocalAppData = fileMetadata.LocalAppData,

            GlobalTransitId = fileMetadata.GlobalTransitId,
            IsEncrypted = fileMetadata.IsEncrypted,
            SenderOdinId = fileMetadata.SenderOdinId,
            OriginalAuthor = fileMetadata.OriginalAuthor,
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

    public static bool TryParseLastModifiedHeader(HttpContentHeaders headers, out UnixTimeUtc? lastModified)
    {
        if (headers?.TryGetValues(HttpHeaderConstants.LastModified, out var values) ?? false)
        {
            var lastModifiedValue = values.FirstOrDefault();

            const string dateTimePattern = "ddd, dd MMM yyyy HH:mm:ss 'GMT'";
            if (DateTimeOffset.TryParseExact(lastModifiedValue, dateTimePattern, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var result))
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

    public static void AssertVersionTagMatch(Guid? currentVersionTag, Guid? versionTagToCompare)
    {
        if (currentVersionTag != versionTagToCompare)
        {
            throw new OdinClientException($"Invalid version tag {versionTagToCompare}", OdinClientErrorCode.VersionTagMismatch);
        }
    }

    public static string GetFileIdForStorage(Guid fileId)
    {
        return $"{fileId.ToString("N").ToLower()}";
    }

    public static string GetPayloadFileExtension(string payloadKey, UnixTimeUtcUnique payloadUid)
    {
        var bn = CreateBasePayloadFileName(payloadKey, payloadUid);
        return $"{bn}.payload";
    }

    public static string GetThumbnailFileExtension(string payloadKey, UnixTimeUtcUnique payloadUid, int width, int height)
    {
        var bn = CreateBasePayloadFileName(payloadKey, payloadUid);
        return $"{bn}{FileNameSectionDelimiter}{width}x{height}.thumb";
    }

    private static string CreateBasePayloadFileName(string payloadKey, UnixTimeUtcUnique payloadUid)
    {
        var parts = new[] { payloadKey, payloadUid.uniqueTime.ToString() };
        return string.Join(FileNameSectionDelimiter, parts.Select(p => p.ToLower()));
    }

    public static SharedSecretEncryptedFileHeader AddIfDeletedNotification(IDriveNotification notification, IOdinContext deviceOdinContext)
    {
        var deletedNotification = notification as DriveFileDeletedNotification;
        if (deletedNotification == null)
        {
            return null;
        }

        return CreateClientFileHeader(deletedNotification.PreviousServerFileHeader, deviceOdinContext);
    }

    public static Guid CreateVersionTag()
    {
        return SequentialGuid.CreateGuid();
    }

    public static void AssertValidAppContentLength(string content)
    {
        OdinValidationUtils.AssertMaxStringLength(content, MaxAppDataContentLength,
            $"local app content is too long; max length is {MaxAppDataContentLength}");
    }

    public static void AssertValidPreviewThumbnail(ThumbnailContent thumbnail)
    {
        OdinValidationUtils.AssertIsTrue((thumbnail?.Content?.Length ?? 0) <= MaxTinyThumbLength,
            $"max preview thumb size is {MaxTinyThumbLength}");
    }
}