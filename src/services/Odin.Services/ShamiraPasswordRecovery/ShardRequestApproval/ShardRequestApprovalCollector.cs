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
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var uid = shardApproval.Id;
        var existingFile = await fileSystem.Query.GetFileByClientUniqueId(driveId, uid, odinContext);
        if (existingFile == null)
        {
            await WriteNewFile(shardApproval, odinContext);
        }
        else
        {
            await OverwriteFile(shardApproval, existingFile.FileId, odinContext);
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
        return files.Select(ToPlayerCollectedShard).ToList();
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
        var content = OdinSystemSerializer.Serialize(shardApproval).ToUtf8ByteArray();

        var fileMetadata = new FileMetadata(file)
        {
            GlobalTransitId = Guid.NewGuid(),
            AppData = new AppFileMetaData()
            {
                FileType = ShardRequestFileType,
                Content = content.ToBase64(),
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

    private async Task OverwriteFile(ShardApprovalRequest dealerShardApproval, Guid existingFileId, IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var file = new InternalDriveFileId()
        {
            FileId = existingFileId,
            DriveId = driveId
        };

        var header = await fileSystem.Storage.GetServerFileHeaderForWriting(file, odinContext);

        var content = OdinSystemSerializer.Serialize(dealerShardApproval).ToUtf8ByteArray();

        header.FileMetadata.AppData.Content = content.ToBase64();
        await fileSystem.Storage.UpdateActiveFileHeader(file, header, odinContext, raiseEvent: true);
    }

    private ShardApprovalRequest ToPlayerCollectedShard(SharedSecretEncryptedFileHeader header)
    {
        var json = header.FileMetadata.AppData.Content.FromBase64().ToStringFromUtf8Bytes();
        return OdinSystemSerializer.Deserialize<ShardApprovalRequest>(json);
    }
}