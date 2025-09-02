using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.ShamiraPasswordRecovery.ShardRequestApproval;

/// <summary>
/// Handles shard requests from dealers
/// </summary>
public class ShardRequestApprovalCollector(StandardFileSystem fileSystem, IMediator mediator)
{
    private const int ShardRequestFileType = 84099;

    /// <summary>
    /// Saves the request for approval
    /// </summary>
    /// <param name="shardApproval"></param>
    /// <param name="odinContext"></param>
    public async Task SaveRequest(ShardApprovalRequest shardApproval, IOdinContext odinContext)
    {
        var targetDrive = SystemDriveConstants.ShardRecoveryDrive;
        var driveId = targetDrive.Alias;
        var uid = shardApproval.Id;

        // we have to grant the dealer write access to the drive since they
        // are coming in authenticated (due to ssl cert) but there is no CAT 

        var contextUpgrade = OdinContextUpgrades.UpgradeToByPassAclCheck(targetDrive, DrivePermission.ReadWrite, odinContext);
        var existingFile = await fileSystem.Query.GetFileByClientUniqueId(driveId, uid, contextUpgrade);
        if (existingFile == null)
        {
            await WriteNewFile(shardApproval, contextUpgrade);
        }
        else
        {
            await OverwriteFile(shardApproval, existingFile.FileId, contextUpgrade);
        }

        await mediator.Publish(new ShamirPasswordRecoveryShardRequestedNotification
        {
            OdinContext = odinContext,
            Sender = shardApproval.Player,
            AdditionalMessage = ""
        });
    }

    public async Task<List<ShardApprovalRequest>> GetRequests(IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsOwner();
        var files = await GetShardRequestFiles(odinContext);
        return files.Select(ToRequest).ToList();
    }

    public async Task DeleteRequest(Guid shardId, IOdinContext odinContext)
    {
        var fileByClientUniqueId = await fileSystem.Query.GetFileByClientUniqueId(
            SystemDriveConstants.ShardRecoveryDrive.Alias,
            shardId,
            odinContext);

        var file = new InternalDriveFileId(SystemDriveConstants.ShardRecoveryDrive.Alias, fileByClientUniqueId.FileId);
        await fileSystem.Storage.HardDeleteLongTermFile(file, odinContext);
    }

    private async Task<List<SharedSecretEncryptedFileHeader>> GetShardRequestFiles(IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;

        var qp = new FileQueryParams()
        {
            FileType = [ShardRequestFileType]
        };

        var options = new QueryBatchResultOptions
        {
            MaxRecords = Int32.MaxValue,
            IncludeHeaderContent = true,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = true,
        };

        var batch = await fileSystem.Query.GetBatch(driveId, qp, options, odinContext);

        return batch.SearchResults.ToList();
    }

    private async Task WriteNewFile(ShardApprovalRequest shardApproval, IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var file = await fileSystem.Storage.CreateInternalFileId(driveId);

        var keyHeader = KeyHeader.Empty();

        var fileMetadata = new FileMetadata(file)
        {
            GlobalTransitId = Guid.NewGuid(),
            AppData = new AppFileMetaData()
            {
                FileType = ShardRequestFileType,
                Content = ShardApprovalRequest.Serialize(shardApproval),
                UniqueId = shardApproval.Id
            },

            IsEncrypted = false,
            VersionTag = SequentialGuid.CreateGuid(),
            Payloads = []
        };

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = AccessControlList.OwnerOnly,
            AllowDistribution = false
        };

        var serverFileHeader = await fileSystem.Storage.CreateServerFileHeader(file,
            keyHeader,
            fileMetadata,
            serverMetadata,
            odinContext);

        await fileSystem.Storage.WriteNewFileHeader(file, serverFileHeader, odinContext, raiseEvent: true);
    }

    private async Task OverwriteFile(ShardApprovalRequest shardApproval, Guid existingFileId, IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var file = new InternalDriveFileId()
        {
            FileId = existingFileId,
            DriveId = driveId
        };

        var header = await fileSystem.Storage.GetServerFileHeaderForWriting(file, odinContext);
        header.FileMetadata.AppData.Content = ShardApprovalRequest.Serialize(shardApproval);
        await fileSystem.Storage.UpdateActiveFileHeader(file, header, odinContext, raiseEvent: true);
    }

    private ShardApprovalRequest ToRequest(SharedSecretEncryptedFileHeader header)
    {
        return ShardApprovalRequest.Deserialize(header.FileMetadata.AppData.Content);
    }
}