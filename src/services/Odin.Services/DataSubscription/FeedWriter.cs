using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Encryption;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage;
using Odin.Services.Drives.FileSystem;


namespace Odin.Services.DataSubscription;

/// <summary>
/// Writes files to the feed drive
/// </summary>
public class FeedWriter(
    ILogger<FeedWriter> logger,
    FileSystemResolver fileSystemResolver,
    IDriveManager driveManager,
    LongTermStorageManager longTermStorageManager)
{
    private readonly IDriveFileSystem _fileSystem = fileSystemResolver.ResolveFileSystem(FileSystemType.Standard);

    public async Task WriteNewFileToFeedDriveAsync(KeyHeader keyHeader, FileMetadata fileMetadata, IOdinContext odinContext)
    {
        // Method assumes you ensured the file was unique by some other method
        var feedDriveId = WellKnownAppDrives.FeedDrive.Alias;
        await _fileSystem.Storage.AssertCanWriteToDrive(feedDriveId, odinContext);

        if (fileMetadata.DataSource == null)
        {
            throw new OdinClientException("RemotePayloadInfo is required");
        }

        if (fileMetadata.GlobalTransitId == null)
        {
            throw new OdinSystemException("File is missing a global transit id");
        }

        var file = await _fileSystem.Storage.CreateInternalFileId(feedDriveId, odinContext);

        // Clearing the UID for any files that go into the feed drive because the feed drive
        // comes from multiple channel drives from many different identities so there could be a clash
        fileMetadata.AppData.UniqueId = null;

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = AccessControlList.OwnerOnly,
            AllowDistribution = false
        };

        var serverFileHeader = await _fileSystem.Storage.CreateServerFileHeader(file, keyHeader, fileMetadata, serverMetadata, odinContext);
        await _fileSystem.Storage.WriteNewFileHeader(file, serverFileHeader, odinContext, raiseEvent: true);
    }

    public async Task ReplaceFileMetadataOnFeedDrive(Guid fileId,
        FileMetadata fileMetadata,
        IOdinContext odinContext,
        OdinId expectedSender,
        KeyHeader keyHeader = null)
    {
        var targetFile = new InternalDriveFileId()
        {
            FileId = fileId,
            DriveId = WellKnownAppDrives.FeedDrive.Alias
        };
        
        await _fileSystem.Storage.AssertCanWriteToDrive(targetFile.DriveId, odinContext);
        var header = await _fileSystem.Storage.GetServerFileHeaderForWriting(targetFile, odinContext);

        if (header == null)
        {
            throw new OdinClientException("Trying to update feed metadata for non-existent file", OdinClientErrorCode.InvalidFile);
        }

        if (header.FileMetadata.FileState == FileState.Deleted)
        {
            return;
        }

        //update the keyheader for scenarios where the feed data was encrypted and the sender changed the keyheader
        if (keyHeader != null)
        {
            header.EncryptedKeyHeader = await _fileSystem.Storage.EncryptKeyHeader(targetFile.DriveId, keyHeader, odinContext);
        }

        var feedDriveId = WellKnownAppDrives.FeedDrive.Alias;
        if (targetFile.DriveId != feedDriveId)
        {
            throw new OdinSystemException("Method cannot be used on drive");
        }

        // The feed drive holds items from every identity the owner follows, so only the identity that
        // delivered an item may update it.  S0510
        if (!SharedSecretEncryptedFileHeader.IsOriginalSender(header.FileMetadata.SenderOdinId, expectedSender))
        {
            logger.LogDebug("ReplaceFileMetadataOnFeedDrive - header file sender ({sender}) did not match expected sender {expected}",
                header.FileMetadata.SenderOdinId,
                expectedSender);
            throw new OdinSecurityException("Invalid caller");
        }

        var localVersionTag = header.FileMetadata.VersionTag;
        var stored = header.FileMetadata;
        header.FileMetadata = fileMetadata; //overwrite with incoming info
        fileMetadata.VersionTag = localVersionTag; // keep this identity's version tag

        // Clearing the UID for any files that go into the feed drive because the feed drive 
        // comes from multiple channel drives from many different identities so there could be a clash
        header.FileMetadata.AppData.UniqueId = null;

        await _fileSystem.Storage.UpdateActiveFileHeader(targetFile, header, odinContext, raiseEvent: true,
            changedByPeer: SendersContentDiffers(stored, fileMetadata));
        if (header.FileMetadata.ReactionPreview == null)
        {
            var drive = await driveManager.GetDriveAsync(targetFile.DriveId);
            await longTermStorageManager.DeleteReactionSummary(drive, targetFile.FileId);
        }
        else
        {
            await _fileSystem.Storage.UpdateReactionSummary(targetFile, header.FileMetadata.ReactionPreview, odinContext);
        }
    }

    /// <summary>
    /// Whether a replace carries a change the sender made to the post itself, which moves TransitUpdated. A sender pushes
    /// an item again for a reaction or a local-only write on its copy, and a follower's catch-up sync rewrites every item;
    /// none of those changes what is compared here. Archival status is left out: the follower may set it on its own copy
    /// </summary>
    private static bool SendersContentDiffers(FileMetadata stored, FileMetadata incoming) =>
        SendersContentOf(stored) != SendersContentOf(incoming);

    private static string SendersContentOf(FileMetadata m) => OdinSystemSerializer.Serialize(new
    {
        m.AppData?.Content,
        m.AppData?.Tags,
        m.AppData?.FileType,
        m.AppData?.DataType,
        m.AppData?.GroupId,
        m.AppData?.UserDate,
        // A sync stores the stand-in for a thumbnail that fails validation, a push the thumbnail itself
        PreviewThumbnail = PreviewThumbnailForFeed(m.AppData?.PreviewThumbnail),
        m.Payloads,
        m.IsEncrypted,
        m.ReferencedFile,
        m.DataSource
    });

    /// <summary>The preview thumbnail a feed item keeps: a stand-in for one that fails validation</summary>
    public static ThumbnailContent PreviewThumbnailForFeed(ThumbnailContent thumbnail) =>
        thumbnail == null || thumbnail.TryValidate() ? thumbnail : InvalidThumbnailStandIn();

    private static ThumbnailContent InvalidThumbnailStandIn() => new()
    {
        PixelWidth = 594,
        PixelHeight = 497,
        ContentType = "image/webp",
        BytesWritten = 0,
        Content = Convert.FromBase64String(
            "UklGRr4CAABXRUJQVlA4WAoAAAAgAAAAEwAAEAAASUNDUMgBAAAAAAHIAAAAAAQwAABtbnRyUkdCIFhZWiAH4AABAAEAAAAAAABhY3NwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAA9tYAAQAAAADTLQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlkZXNjAAAA8AAAACRyWFlaAAABFAAAABRnWFlaAAABKAAAABRiWFlaAAABPAAAABR3dHB0AAABUAAAABRyVFJDAAABZAAAAChnVFJDAAABZAAAAChiVFJDAAABZAAAAChjcHJ0AAABjAAAADxtbHVjAAAAAAAAAAEAAAAMZW5VUwAAAAgAAAAcAHMAUgBHAEJYWVogAAAAAAAAb6IAADj1AAADkFhZWiAAAAAAAABimQAAt4UAABjaWFlaIAAAAAAAACSgAAAPhAAAts9YWVogAAAAAAAA9tYAAQAAAADTLXBhcmEAAAAAAAQAAAACZmYAAPKnAAANWQAAE9AAAApbAAAAAAAAAABtbHVjAAAAAAAAAAEAAAAMZW5VUwAAACAAAAAcAEcAbwBvAGcAbABlACAASQBuAGMALgAgADIAMAAxADZWUDgg0AAAAHAFAJ0BKhQAEQA+9XKwUyqmpKKoCAFQHolnAM45i2X0kIuwndxsFE34nVBHGFszwHZV8SgAyzpLin+kW9ywqKsxofEadS2Rip94f5OHdFtM/yYfsrr7xi10XLJmghR4yV+C+ZMFqn3OT/BKMJhzf/SbgkjMD0Hik4z7sPZIfYFx8/a1qPW5hS1TbWtU6vP3cO/OORwki96rapqidTKwBau/DQMJqMCXqYjw+YbZcZXvUmuGmgiF3lz2Yh1jP0Oz1HgZE/JB3wOOhJwtKkAAAAA=")
    };

    public async Task RemoveFeedDriveFile(InternalDriveFileId file, IOdinContext odinContext)
    {
        await _fileSystem.Storage.AssertCanWriteToDrive(file.DriveId, odinContext);
        var header = await _fileSystem.Storage.GetServerFileHeaderForWriting(file, odinContext);
        var feedDriveId = WellKnownAppDrives.FeedDrive.Alias;

        if (file.DriveId != feedDriveId)
        {
            throw new OdinSystemException("Method cannot be used on drive");
        }

        //S0510
        if (header.FileMetadata.SenderOdinId != odinContext.GetCallerOdinIdOrFail())
        {
            throw new OdinSecurityException("Invalid caller");
        }

        await _fileSystem.Storage.SoftDeleteLongTermFile(file, odinContext, null);
    }
}