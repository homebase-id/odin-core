using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Authentication.Owner;
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
    PasswordKeyRecoveryService passwordKeyRecoveryService,
    PeerOutgoingTransferService transferService,
    StandardFileSystem fileSystem)
{
    public const int DealerShardConfigFiletype = 44532;
    public const int PlayerEncryptedShardFileType = 74829;
    public const string DealerShardConfigUid = "88be2b93-a5af-4884-adff-c73b2c9b04d4";

    public async Task UpdateShards(List<ShamiraPlayer> players,
        int totalShards,
        int minShards,
        IOdinContext odinContext)
    {

        //TODO: which key do we want to send off?
        // for now we'll send off the recovery key
        var k = await passwordKeyRecoveryService.GetKeyAsync(odinContext);
        var secret = BIP39Util.DecodeBIP39(k.Key);
        
        var r = await CreateShards(players,
            totalShards,
            minShards,
            secret,
            odinContext);

        var package = new DealerShardPackage
        {
            Envelopes = r.DealerEnvelops
        };

        await SaveDealerEnvelop(package, odinContext);
        await EnqueueShardsForDistribution(r.PlayerShards, odinContext);
    }
    
    public async Task<DealerShardPackage> GetDealerEnvelop(IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var uid = Guid.Parse(DealerShardConfigUid);
        var existingFile = await fileSystem.Query.GetFileByClientUniqueId(driveId, uid, odinContext);

        if (null == existingFile)
        {
            return null;
        }

        var key = odinContext.PermissionsContext.SharedSecretKey;
        var decryptedKeyHeader = existingFile.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref key);

        var bytes = decryptedKeyHeader.Decrypt(existingFile.FileMetadata.AppData.Content.FromBase64());
        var json = bytes.ToStringFromUtf8Bytes();

        var package = OdinSystemSerializer.Deserialize<DealerShardPackage>(json);
        return package;
    }


    /// <summary>
    /// Creates encrypted shards for the specified players
    /// </summary>
    private Task<(List<DealerShardEnvelope> DealerEnvelops, List<PlayerEncryptedShard> PlayerShards)> CreateShards(
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

    private async Task SaveDealerEnvelop(DealerShardPackage envelope, IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var uid = Guid.Parse(DealerShardConfigUid);
        var existingFile = await fileSystem.Query.GetFileByClientUniqueId(driveId, uid, odinContext);
        if (existingFile == null)
        {
            await WriteNewDealerEnvelopeFile(envelope, odinContext);
        }
        else
        {
            await OverwriteDealerEnvelopeFile(envelope, existingFile.FileId, odinContext);
        }
    }

    /// <summary>
    /// Sends the shards to the list identities
    /// </summary>
    /// <param name="shards"></param>
    /// <param name="odinContext"></param>
    private async Task<Dictionary<OdinId, TransferStatus>> EnqueueShardsForDistribution(List<PlayerEncryptedShard> shards, IOdinContext odinContext)
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
    
    private async Task<ServerFileHeader> WriteNewDealerEnvelopeFile(DealerShardPackage dealerShard, IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var file = await fileSystem.Storage.CreateInternalFileId(driveId);

        var keyHeader = KeyHeader.NewRandom16();
        var encryptedContent = keyHeader.EncryptDataAes(OdinSystemSerializer.Serialize(dealerShard).ToUtf8ByteArray());

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
            AllowDistribution = false
        };

        var serverFileHeader = await fileSystem.Storage.CreateServerFileHeader(file, keyHeader, fileMetadata, serverMetadata, odinContext);
        await fileSystem.Storage.WriteNewFileHeader(file, serverFileHeader, odinContext, raiseEvent: true);

        return serverFileHeader;
    }

    private async Task<ServerFileHeader> OverwriteDealerEnvelopeFile(DealerShardPackage dealerShard, Guid existingFileId,
        IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var file = new InternalDriveFileId()
        {
            FileId = existingFileId,
            DriveId = driveId
        };

        var header = await fileSystem.Storage.GetServerFileHeaderForWriting(file, odinContext);

        // todo: should I rotate the key? 
        // header.EncryptedKeyHeader = await fileSystem.Storage.EncryptKeyHeader(file.DriveId, keyHeader, odinContext);

        // decrypt the key header so we can encrypt the content
        var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(driveId);
        var keyHeader = header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);

        var encryptedContent = keyHeader.EncryptDataAes(OdinSystemSerializer.Serialize(dealerShard).ToUtf8ByteArray());

        header.FileMetadata.AppData.Content = encryptedContent.ToBase64();
        await fileSystem.Storage.UpdateActiveFileHeader(file, header, odinContext, raiseEvent: true);
        return header;
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