using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Services.ShamiraPasswordRecovery;

public class ShamiraRecoveryService(
    TableKeyValue tblKeyValue,
    PeerOutgoingTransferService transferService,
    StandardFileSystem fileSystem)
{
    public const int DealerShardConfigFiletype = 44532;
    public const int PlayerEncryptedShardFileType = 74829;
    public const string DealerShardConfigUid = "88be2b93-a5af-4884-adff-c73b2c9b04d4";

    /// <summary>
    /// Creates encrypted shards for the specified players
    /// </summary>
    public Task<(
            List<DealerShardEnvelope> DealerRecords,
            List<PlayerEncryptedShard> PlayerRecords)>
        CreateShards(
            List<ShamiraPlayer> players,
            int totalShards,
            int minShards,
            SensitiveByteArray secret,
            IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        var shards = ShamirSecretSharing.GenerateShamirShares(totalShards, minShards, secret.GetKey());

        OdinValidationUtils.AssertIsTrue(players.TrueForAll(p => p.Type == PlayerType.Delegate), "Only Delegate player type is supported");
        OdinValidationUtils.AssertIsTrue(shards.Count == players.Count, "Player and shard count do not match");

        var dealerRecords = new List<DealerShardEnvelope>();
        var playerRecords = new List<PlayerEncryptedShard>();

        // give each player a shard; encrypted with a key
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            var playerEncryptionKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            // Dealer encrypts a player's shard with the random key
            var (iv, cipher) = AesCbc.Encrypt(shards[i].Shard, playerEncryptionKey);

            var playerShardId = Guid.NewGuid();
            playerRecords.Add(new PlayerEncryptedShard(playerShardId, player, cipher));
            dealerRecords.Add(new DealerShardEnvelope()
            {
                Player = player,
                ShardId = playerShardId,
                EncryptionKey = playerEncryptionKey.GetKey(),
                EncryptionIv = iv
            });
        }

        return Task.FromResult((dealerRecords, playerRecords));
    }

    public async Task SaveDealerEnvelop(ShardEnvelop envelope, IOdinContext odinContext)
    {
        await WriteDealerEnvelope(envelope, odinContext);
    }

    /// <summary>
    /// Sends the shards to the list identities
    /// </summary>
    /// <param name="shards"></param>
    /// <param name="odinContext"></param>
    public async Task<Dictionary<OdinId, TransferStatus>> DistributeShards(List<PlayerEncryptedShard> shards, IOdinContext odinContext)
    {
        var results = new Dictionary<OdinId, TransferStatus>();

        foreach (var shard in shards)
        {
            var header = await WritePlayerEncryptedShardToTempDrive(shard, odinContext);
            var transitOptions = new TransitOptions
            {
                IsTransient = true,
                Recipients = [shard.Player.OdinId.DomainName],
                DisableTransferHistory = true,
                UseAppNotification = false,
                AppNotificationOptions = null,
                RemoteTargetDrive = SystemDriveConstants.ShardRecoveryDrive,
                Priority = OutboxPriority.High
            };

            var transferStatusMap = await transferService.SendFile(
                header.FileMetadata.File,
                transitOptions,
                TransferFileType.Normal,
                header.ServerMetadata.FileSystemType,
                odinContext);

            results.Add(shard.Player.OdinId, transferStatusMap[shard.Player.OdinId]);
        }

        return results;
    }

    private async Task<ServerFileHeader> WriteDealerEnvelope(ShardEnvelop shard, IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var file = await fileSystem.Storage.CreateInternalFileId(driveId);

        var keyHeader = KeyHeader.NewRandom16();
        var encryptedContent = keyHeader.EncryptDataAes(OdinSystemSerializer.Serialize(shard).ToUtf8ByteArray());

        var fileMetadata = new FileMetadata(file)
        {
            GlobalTransitId = Guid.NewGuid(),
            AppData = new AppFileMetaData()
            {
                FileType = DealerShardConfigFiletype,
                Content = encryptedContent.ToBase64(),
                UniqueId = Guid.Parse(DealerShardConfigUid)
            },

            IsEncrypted = true,
            VersionTag = SequentialGuid.CreateGuid(),
            Payloads = []
        };

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = AccessControlList.OwnerOnly,
            AllowDistribution = true
        };

        fileSystem.Storage.OverwriteMetadata()
        var serverFileHeader = await fileSystem.Storage.CreateServerFileHeader(file, keyHeader, fileMetadata, serverMetadata, odinContext);
        await fileSystem.Storage.WriteNewFileHeader(file, serverFileHeader, odinContext, raiseEvent: true);

        return serverFileHeader;
    }


    private async Task<ServerFileHeader> WritePlayerEncryptedShardToTempDrive(PlayerEncryptedShard shard, IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.TransientTempDrive.Alias;
        var file = await fileSystem.Storage.CreateInternalFileId(driveId);

        var keyHeader = KeyHeader.Empty();
        var shardData = shard.DealerEncryptedShard;

        var fileMetadata = new FileMetadata(file)
        {
            GlobalTransitId = Guid.NewGuid(),
            AppData = new AppFileMetaData()
            {
                FileType = PlayerEncryptedShardFileType,
                Content = shardData.ToBase64(),
                UniqueId = shard.Id
            },

            IsEncrypted = false,
            SenderOdinId = odinContext.Tenant,
            VersionTag = SequentialGuid.CreateGuid(),
            Payloads = []
        };

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = AccessControlList.OwnerOnly,
            AllowDistribution = true
        };

        var serverFileHeader = await fileSystem.Storage.CreateServerFileHeader(file, keyHeader, fileMetadata, serverMetadata, odinContext);
        await fileSystem.Storage.WriteNewFileHeader(file, serverFileHeader, odinContext, raiseEvent: true);

        return serverFileHeader;
    }
}