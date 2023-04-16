using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drives.FileSystem.Base;

public static class Utility
{
    /// <summary>
    /// Converts the ServerFileHeader to a SharedSecretEncryptedHeader
    /// </summary>
    public static SharedSecretEncryptedFileHeader ConvertToSharedSecretEncryptedClientFileHeader(ServerFileHeader header, DotYouContextAccessor contextAccessor, bool forceIncludeServerMetadata = false)
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
            Priority = priority
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
            ContentType = fileMetadata.ContentType,
            GlobalTransitId = fileMetadata.GlobalTransitId,
            PayloadSize = fileMetadata.PayloadSize,
            OriginalRecipientList = fileMetadata.OriginalRecipientList,
            PayloadIsEncrypted = fileMetadata.PayloadIsEncrypted,
            SenderOdinId = fileMetadata.SenderOdinId,
            ReferencedFile = fileMetadata.ReferencedFile,
            ReactionPreview = fileMetadata.ReactionPreview,
            VersionTag = fileMetadata.VersionTag.GetValueOrDefault()
        };
        return clientFile;
    }
}