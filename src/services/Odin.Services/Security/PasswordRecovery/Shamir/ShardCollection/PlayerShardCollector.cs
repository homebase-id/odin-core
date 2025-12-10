using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Security.PasswordRecovery.Shamir.ShardCollection;

/// <summary>
/// Handles shards collected from players during recovery
/// </summary>
public class PlayerShardCollector(StandardFileSystem fileSystem)
{
    private const int CollectedPlayerShardFileType = 84001;

    public async Task SavePlayerShard(PlayerEncryptedShard shard, IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var uid = shard.Id;
        
        var existingFile = await fileSystem.Query.GetFileByClientUniqueIdForWriting(driveId, uid, odinContext);
        
        if (existingFile == null)
        {
            await WriteNewFile(shard, odinContext);
        }
        else
        {
            await OverwriteFile(shard, existingFile.FileId, odinContext);
        }
    }

    public async Task<List<PlayerEncryptedShard>> GetCollectShards(IOdinContext odinContext)
    {
        var byPassAclCheckContext = OdinContextUpgrades.UpgradeToByPassAclCheck(SystemDriveConstants.ShardRecoveryDrive,
            DrivePermission.Read, odinContext);
        var files = await GetShardFiles(byPassAclCheckContext);
        return files.Select(ToPlayerCollectedShard).ToList();
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

        var files = await GetShardFiles(byPassAclCheckContext);
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        foreach (var f in files)
        {
            var file = new InternalDriveFileId(driveId, f.FileId);
            await fileSystem.Storage.HardDeleteLongTermFile(file, byPassAclCheckContext);
        }
    }

    private async Task<List<SharedSecretEncryptedFileHeader>> GetShardFiles(IOdinContext odinContext)
    {
        var byPassAclCheckContext = OdinContextUpgrades.UpgradeToByPassAclCheck(
            SystemDriveConstants.ShardRecoveryDrive,
            DrivePermission.Read,
            odinContext);


        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;

        var qp = new FileQueryParams()
        {
            FileType = [CollectedPlayerShardFileType]
        };

        var options = new QueryBatchResultOptions
        {
            MaxRecords = Int32.MaxValue,
            IncludeHeaderContent = true,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = true,
        };

        var batch = await fileSystem.Query.GetBatch(driveId, qp, options, byPassAclCheckContext);

        return batch.SearchResults.ToList();
    }

    private async Task WriteNewFile(PlayerEncryptedShard shard, IOdinContext odinContext)
    {
        var byPassAclCheckContext = OdinContextUpgrades.UpgradeToByPassAclCheck(SystemDriveConstants.ShardRecoveryDrive,
            DrivePermission.Write, odinContext);

        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var file = await fileSystem.Storage.CreateInternalFileId(driveId, odinContext);

        var keyHeader = KeyHeader.Empty();
        var fileMetadata = new FileMetadata(file)
        {
            GlobalTransitId = Guid.NewGuid(),
            AppData = new AppFileMetaData()
            {
                FileType = CollectedPlayerShardFileType,
                Content = PlayerEncryptedShard.Serialize(shard),
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

    private async Task OverwriteFile(PlayerEncryptedShard shard, Guid existingFileId,
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
        header.FileMetadata.AppData.Content = PlayerEncryptedShard.Serialize(shard);
        await fileSystem.Storage.UpdateActiveFileHeader(file, header, byPassAclCheckContext, raiseEvent: true);
    }

    private PlayerEncryptedShard ToPlayerCollectedShard(SharedSecretEncryptedFileHeader header)
    {
        return PlayerEncryptedShard.Deserialize(header.FileMetadata.AppData.Content);
    }
}