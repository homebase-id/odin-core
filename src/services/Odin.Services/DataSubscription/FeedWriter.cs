using System.Threading.Tasks;
using Odin.Core.Exceptions;
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
    ILogger logger,
    FileSystemResolver fileSystemResolver,
    IDriveManager driveManager,
    LongTermStorageManager longTermStorageManager)
{
    private readonly IDriveFileSystem _fileSystem = fileSystemResolver.ResolveFileSystem(FileSystemType.Standard);

    public async Task WriteNewFileToFeedDriveAsync(KeyHeader keyHeader, FileMetadata fileMetadata, IOdinContext odinContext)
    {
        // Method assumes you ensured the file was unique by some other method
        var feedDriveId = SystemDriveConstants.FeedDrive.Alias;
        await _fileSystem.Storage.AssertCanWriteToDrive(feedDriveId, odinContext);
     
        if (fileMetadata.RemotePayloadInfo == null)
        {
            throw new OdinClientException("RemotePayloadInfo is required");
        }
        
        if (fileMetadata.GlobalTransitId == null)
        {
            throw new OdinSystemException("File is missing a global transit id");
        }
        
        var file = await _fileSystem.Storage.CreateInternalFileId(feedDriveId);

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

    public async Task ReplaceFileMetadataOnFeedDrive(InternalDriveFileId targetFile,
        FileMetadata fileMetadata,
        IOdinContext odinContext,
        bool bypassCallerCheck = false,
        KeyHeader keyHeader = null)
    {
        await _fileSystem.Storage.AssertCanWriteToDrive(targetFile.DriveId, odinContext);
        var header = await _fileSystem.Storage.GetServerFileHeader(targetFile, odinContext);

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

        var feedDriveId = SystemDriveConstants.FeedDrive.Alias;
        if (targetFile.DriveId != feedDriveId)
        {
            throw new OdinSystemException("Method cannot be used on drive");
        }

        if (!bypassCallerCheck) //eww: this allows the follower service to synchronize files when you start following someone.
        {
            //S0510
            if (header.FileMetadata.SenderOdinId != odinContext.GetCallerOdinIdOrFail())
            {
                logger.LogDebug("ReplaceFileMetadataOnFeedDrive - header file sender ({sender}) did not match context sender {ctx}",
                    header.FileMetadata.SenderOdinId,
                    odinContext.GetCallerOdinIdOrFail());
                throw new OdinSecurityException("Invalid caller");
            }
        }

        var localVersionTag = header.FileMetadata.VersionTag;
        header.FileMetadata = fileMetadata; //overwrite with incoming info
        fileMetadata.VersionTag = localVersionTag; // keep this identity's version tag

        // Clearing the UID for any files that go into the feed drive because the feed drive 
        // comes from multiple channel drives from many different identities so there could be a clash
        header.FileMetadata.AppData.UniqueId = null;

        await _fileSystem.Storage.UpdateActiveFileHeader(targetFile, header, odinContext, raiseEvent: true);
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

    public async Task RemoveFeedDriveFile(InternalDriveFileId file, IOdinContext odinContext)
    {
        await _fileSystem.Storage.AssertCanWriteToDrive(file.DriveId, odinContext);
        var header = await _fileSystem.Storage.GetServerFileHeader(file, odinContext);
        var feedDriveId = SystemDriveConstants.FeedDrive.Alias;

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