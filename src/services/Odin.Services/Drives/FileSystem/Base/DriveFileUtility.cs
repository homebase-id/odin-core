using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NodaTime;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Shares;
using Odin.Services.Mediator;
using Odin.Services.Peer.Encryption;
using Odin.Services.Util;

namespace Odin.Services.Drives.FileSystem.Base;

public static class DriveFileUtility
{
    //public const string ValidPayloadKeyRegex = @"^[a-z0-9_]{8,10}$";

    // public const int MaxAppDataContentLength = 10 * 1024; MOVED TO AppMetaData.MaxAppDataContentLength
    // public const int MaxLocalAppDataContentLength = 4 * 1024; MOVED TO LocalAppMetaData.MaxLocalAppDataContentLength
    // public const int MaxTinyThumbLength = 1 * 1024;  MOVED TO ThumbNailContent.MaxTinyThumbLength


    /// <summary>
    /// A stored aes key header, opened with the drive's storage key the caller holds; the copy of the storage
    /// key is wiped.  Not for a client-locked one, which the server can't open (<see cref="ZkDriveFiles.IsClientLocked"/>).
    /// </summary>
    public static KeyHeader OpenWithStorageKey(ServerFileHeader header, IOdinContext odinContext)
    {
        var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(header.FileMetadata.File.DriveId);
        try
        {
            return header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
        }
        finally
        {
            storageKey.Wipe();
        }
    }

    /// <summary>
    /// Converts the ServerFileHeader to a SharedSecretEncryptedHeader
    /// </summary>
    /// <param name="callerShare">On a Selective Share drive, the live share the read rule let an outside caller in
    /// through: the header comes with that share, and an encrypted file's key header is the share's locked file key
    /// with the file's IV (docs/selective-share-pr3-plan.md, section 6), never the file's own.</param>
    public static SharedSecretEncryptedFileHeader CreateClientFileHeader(ServerFileHeader header, IOdinContext odinContext,
        bool forceIncludeServerMetadata = false, DriveAclIndexRecord callerShare = null)
    {
        if (header == null)
        {
            return null;
        }

        EncryptedKeyHeader sharedSecretEncryptedKeyHeader;
        if (callerShare != null)
        {
            sharedSecretEncryptedKeyHeader = header.FileMetadata.IsEncrypted
                ? EncryptedKeyHeader.ForShare(header.EncryptedKeyHeader.Iv, callerShare.lockedFileKey)
                : EncryptedKeyHeader.Empty();
        }
        else if (ZkDriveFiles.IsClientLocked(header))
        {
            // Returned as stored, sealed or zkDrive: only a device holding the drive's keys opens it.  A
            // backstop: the read rule (IDriveAclAuthorizationService.CallerMayReadAsync) has already left out a
            // file this refuses
            ZkDriveFiles.AssertVisibleTo(header, odinContext);
            sharedSecretEncryptedKeyHeader = header.EncryptedKeyHeader;
        }
        else if (header.FileMetadata.IsEncrypted)
        {
            var keyHeader = OpenWithStorageKey(header, odinContext);
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

        // The member sees only their own share: its bits and expiry, for the client to show
        if (callerShare != null)
        {
            clientFileHeader.Shares = [ShareRow.From(callerShare)];
        }

        return clientFileHeader;
    }

    /// <summary>
    /// Gets the <see cref="EncryptedKeyHeader"/> for the payload key using the payload's IV; null for a
    /// zero-knowledge drive's file, whose key header the server can't open
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

        if (ZkDriveFiles.IsClientLocked(header))
        {
            // No key header: the client opens a payload with the file's AES key and the payload's IV.  The read rule ran
            // when the header was read, and lets in a member of a Selective Share circle too (section 6), so there is
            // nothing to hand out or guard here
            return null;
        }

        if (header.FileMetadata.IsEncrypted)
        {
            var keyHeader = OpenWithStorageKey(header, odinContext);
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
            VersionTag = fileMetadata.VersionTag.GetValueOrDefault(),
            DataSource = fileMetadata.DataSource,
            Ttl = fileMetadata.Ttl
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

        int marginWidth = (width * 91) / 100;
        int marginHeight = (height * 91) / 100;

        var sorted = thumbs.OrderBy(t => t.PixelWidth * t.PixelHeight).ToList();
        var nextSizeUp = sorted.FirstOrDefault(t => t.PixelHeight >= marginHeight && t.PixelWidth >= marginWidth);

        if (null == nextSizeUp)
        {
            return sorted.LastOrDefault();
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

    public static string GetPayloadFileExtensionStarStar()
    {
        var bn = TenantPathManager.GetBasePayloadSearchMask();
        var r = $"{bn}{TenantPathManager.PayloadExtension}";

        return r;
    }
    
    public static Guid CreateVersionTag()
    {
        return SequentialGuid.CreateGuid();
    }
}