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
    /// <param name="shard"></param>
    /// <param name="odinContext"></param>
    public async Task SaveRequest(PlayerEncryptedShard shard, IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var uid = shard.Id;
        var existingFile = await fileSystem.Query.GetFileByClientUniqueId(driveId, uid, odinContext);
        if (existingFile == null)
        {
            await WriteNewFile(shard, odinContext);
        }
        else
        {
            await OverwriteFile(shard, existingFile.FileId, odinContext);
        }

        await mediator.Publish(new ShamirPasswordRecoveryShardRequestedNotification
        {
            OdinContext = odinContext,
            Sender = shard.Player.OdinId,
            AdditionalMessage = ""
        });
    }

    public async Task<List<PlayerEncryptedShard>> GetRequests(IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsOwner();
        var files = await GetShardRequestFiles(odinContext);
        return files.Select(ToPlayerCollectedShard).ToList();
    }


    public async Task ApproveShardRequest(Guid shardId, IOdinContext odinContext)
    {
        await Task.CompletedTask;
        
        // get the shard

        // send it to the dealer via https 

        // Note: what happens when they are no longer connected?
    }
    
    /// <summary>
    /// Deletes all collected shards
    /// </summary>
    public async Task DeleteCollectedShards(IOdinContext odinContext)
    {
        var byPassAclCheckContext = OdinContextUpgrades.UpgradeToByPassAclCheck(
            SystemDriveConstants.ShardRecoveryDrive,
            DrivePermission.ReadWrite,
            odinContext);

        var files = await GetShardRequestFiles(byPassAclCheckContext);
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        foreach (var f in files)
        {
            var file = new InternalDriveFileId(driveId, f.FileId);
            await fileSystem.Storage.HardDeleteLongTermFile(file, byPassAclCheckContext);
        }
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

    private async Task WriteNewFile(PlayerEncryptedShard shard, IOdinContext odinContext)
    {
        var byPassAclCheckContext = OdinContextUpgrades.UpgradeToByPassAclCheck(SystemDriveConstants.ShardRecoveryDrive,
            DrivePermission.Write, odinContext);

        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var file = await fileSystem.Storage.CreateInternalFileId(driveId);

        var keyHeader = KeyHeader.Empty();
        var content = OdinSystemSerializer.Serialize(shard).ToUtf8ByteArray();

        var fileMetadata = new FileMetadata(file)
        {
            GlobalTransitId = Guid.NewGuid(),
            AppData = new AppFileMetaData()
            {
                FileType = ShardRequestFileType,
                Content = content.ToBase64(),
                UniqueId = shard.Id
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

        var serverFileHeader = await fileSystem.Storage.CreateServerFileHeader(file, keyHeader, fileMetadata, serverMetadata,
            byPassAclCheckContext);
        await fileSystem.Storage.WriteNewFileHeader(file, serverFileHeader, byPassAclCheckContext, raiseEvent: true);
    }

    private async Task OverwriteFile(PlayerEncryptedShard dealerShard, Guid existingFileId,
        IOdinContext odinContext)
    {
        var byPassAclCheckContext = OdinContextUpgrades.UpgradeToByPassAclCheck(SystemDriveConstants.ShardRecoveryDrive,
            DrivePermission.Write, odinContext);

        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var file = new InternalDriveFileId()
        {
            FileId = existingFileId,
            DriveId = driveId
        };

        var header = await fileSystem.Storage.GetServerFileHeaderForWriting(file, byPassAclCheckContext);

        var content = OdinSystemSerializer.Serialize(dealerShard).ToUtf8ByteArray();

        header.FileMetadata.AppData.Content = content.ToBase64();
        await fileSystem.Storage.UpdateActiveFileHeader(file, header, byPassAclCheckContext, raiseEvent: true);
    }

    private PlayerEncryptedShard ToPlayerCollectedShard(SharedSecretEncryptedFileHeader header)
    {
        var json = header.FileMetadata.AppData.Content.FromBase64().ToStringFromUtf8Bytes();
        return OdinSystemSerializer.Deserialize<PlayerEncryptedShard>(json);
    }
}