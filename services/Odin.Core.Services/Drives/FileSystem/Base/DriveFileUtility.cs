using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Primitives;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Time;
using Refit;

namespace Odin.Core.Services.Drives.FileSystem.Base;

public static class DriveFileUtility
{
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
        if (header.FileMetadata.PayloadIsEncrypted)
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
            FileState = header.FileMetadata.FileState,
            FileSystemType = header.ServerMetadata.FileSystemType,
            SharedSecretEncryptedKeyHeader = sharedSecretEncryptedKeyHeader,
            FileMetadata = RedactFileMetadata(header.FileMetadata),
            Priority = priority,
            FileByteCount = header.ServerMetadata.FileByteCount,
            Payloads = header.FileMetadata.Payloads
        };

        //add additional info
        if (contextAccessor.GetCurrent().Caller.IsOwner || forceIncludeServerMetadata)
        {
            clientFileHeader.ServerMetadata = header.ServerMetadata;
        }

        return clientFileHeader;
    }

    private static ClientFileMetadata RedactFileMetadata(FileMetadata fileMetadata)
    {
        var clientFile = new ClientFileMetadata
        {
            Created = fileMetadata.Created,
            Updated = fileMetadata.Updated,
            AppData = fileMetadata.AppData,
            GlobalTransitId = fileMetadata.GlobalTransitId,
            PayloadSize = fileMetadata.PayloadSize,
            OriginalRecipientList = fileMetadata.OriginalRecipientList,
            PayloadIsEncrypted = fileMetadata.PayloadIsEncrypted,
            SenderOdinId = fileMetadata.SenderOdinId,
            ReferencedFile = fileMetadata.ReferencedFile,
            ReactionPreview = fileMetadata.ReactionPreview,
            Payloads = fileMetadata.Payloads,
            VersionTag = fileMetadata.VersionTag.GetValueOrDefault()
        };
        return clientFile;
    }

    public static ImageDataHeader FindMatchingThumbnail(List<ImageDataHeader> thumbs, int width, int height, bool directMatchOnly)
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
        string extenstion = $"-{key?.ToLower()}.payload";
        return extenstion;
    }

    public static bool TryParseLastModifiedHeader(HttpContentHeaders headers, out UnixTimeUtc? lastModified)
    {
        if (headers?.TryGetValues(HttpHeaderConstants.LastModified, out var values) ?? false)
        {
            var lastModifiedValue = values.FirstOrDefault();

            if (lastModifiedValue != null && DateTime.TryParse(lastModifiedValue, out var lastModifiedDateTime))
            {
                lastModified = UnixTimeUtc.FromDateTime(lastModifiedDateTime);
                return true;
            }
        }

        lastModified = null;
        return false;
    }

    public static string GetLastModifiedHeaderValue(UnixTimeUtc? lastModified)
    {
        return lastModified.GetValueOrDefault().ToDateTime().ToString("R");
    }
}