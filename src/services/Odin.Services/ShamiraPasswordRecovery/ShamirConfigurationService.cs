using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Services.ShamiraPasswordRecovery;

public class ShamirConfigurationService(
    PasswordKeyRecoveryService passwordKeyRecoveryService,
    PeerOutgoingTransferService transferService,
    CircleNetworkService circleNetworkService,
    IOdinHttpClientFactory odinHttpClientFactory,
    TableKeyValue tblKeyValue,
    IdentityDatabase db,
    StandardFileSystem fileSystem)
{
    private const int DealerShardConfigFiletype = 44532;
    private const int PlayerEncryptedShardFileType = 74829;
    private const string DealerShardConfigUid = "88be2b93-a5af-4884-adff-c73b2c9b04d4";

    private static readonly Guid ShamirRecordStorageId = Guid.Parse("39c98971-ac39-4b4b-8eee-68a1742203c6");
    private const string ContextKey = "078e018e-e6b3-4349-b635-721b43d35241";
    private static readonly SingleKeyValueStorage Storage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ContextKey));

    public async Task ConfigureShards(List<ShamiraPlayer> players, int minShards, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(players, nameof(players));
        OdinValidationUtils.AssertIsTrue(players.Count >= 3, "You need at least 3 players");
        OdinValidationUtils.AssertIsTrue(players.Count >= minShards, "The number of players must be greater than min shards");
        OdinValidationUtils.AssertValidRecipientList(players.Select(p => p.OdinId), false, odinContext.Tenant);

        var distributionKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

        var r = await CreateShards(players,
            minShards,
            distributionKey,
            odinContext);

        var package = new DealerShardPackage
        {
            MinMatchingShards = minShards,
            Envelopes = r.DealerEnvelops
        };

        await using var tx = await db.BeginStackedTransactionAsync();

        await SaveDealerPackage(package, odinContext);

        var enqueueResults = await EnqueueShardsForDistribution(r.PlayerShards, odinContext);
        var failures = enqueueResults.Where(kvp => kvp.Value != TransferStatus.Enqueued).ToList();
        if (failures.Any())
        {
            throw new OdinClientException($"Failed to enqueue shards for identities [{string.Join(",", failures.Select(f => f.Key))}]");
        }

        await SaveDistributableKey(distributionKey, package, odinContext);

        tx.Commit();

        distributionKey.Wipe();
    }

    /// <summary>
    /// Verifies shards held by players
    /// </summary>
    public async Task<RemoteShardVerificationResult> VerifyRemotePlayerShards(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        // get the preconfigured package
        var package = await this.GetDealerShardPackage(odinContext);

        if (package == null)
        {
            throw new OdinClientException("Sharding not configured");
        }

        var results = new Dictionary<string, ShardVerificationResult>();
        foreach (var envelope in package.Envelopes)
        {
            var result = await VerifyRemotePlayer(envelope.Player.OdinId, envelope.ShardId, odinContext);
            results.Add(envelope.Player.OdinId, result);
        }

        return new RemoteShardVerificationResult()
        {
            Players = results
        };
    }

    public async Task<ShardVerificationResult> VerifyRemotePlayer(OdinId player, Guid shardId, IOdinContext odinContext)
    {
        var (_, client) = await CreateClientAsync(player, odinContext);
        var response = await client.VerifyShard(new VerifyShardRequest()
        {
            ShardId = shardId
        });

        if (response.IsSuccessStatusCode)
        {
            return response.Content;
        }

        return new ShardVerificationResult()
        {
            IsValid = false
        };
    }

    /// <summary>
    /// Verifies the shard given to this identity from a dealer
    /// </summary>
    public async Task<ShardVerificationResult> VerifyDealerShard(Guid shardId, IOdinContext odinContext)
    {
        var (_, _, verifyDealerShard) = await GetDealerShard(shardId, odinContext);
        return verifyDealerShard;
    }

    public async Task<DealerShardConfig> GetRedactedConfig(IOdinContext odinContext)
    {
        var package = await this.GetDealerShardPackage(odinContext);

        if (package == null)
        {
            return null;
        }

        return new DealerShardConfig()
        {
            MinMatchingShards = package.MinMatchingShards,
            Envelopes = package.Envelopes.Select(e => new DealerShardEnvelopeRedacted()
            {
                ShardId = e.ShardId,
                Player = e.Player
            }).ToList(),
            Created = package.Created
        };
    }

    public async Task<List<ShamiraPlayer>> GetPlayers(IOdinContext odinContext)
    {
        var key = await this.GetKeyInternalAsync();
        if (key == null)
        {
            return [];
        }

        return key.Players;
    }

    public async Task<(OdinId dealer, PlayerEncryptedShard shard, ShardVerificationResult verificationResult)> GetDealerShard(Guid shardId,
        IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var dealer = odinContext.Caller.OdinId.GetValueOrDefault();

        var options = new ResultOptions
        {
            MaxRecords = 1,
            IncludeHeaderContent = true,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = false,
            IncludeTransferHistory = false
        };

        var byPassAclCheckContext = OdinContextUpgrades.UpgradeToByPassAclCheck(SystemDriveConstants.ShardRecoveryDrive,
            DrivePermission.Read, odinContext);
        var file = await fileSystem.Query.GetFileByClientUniqueId(driveId, shardId, options, byPassAclCheckContext);

        var isValid = file != null &&
                      !string.IsNullOrEmpty(file.FileMetadata.AppData.Content) &&
                      file.FileMetadata.SenderOdinId == dealer;

        if (!isValid)
        {
            return (dealer, null, new ShardVerificationResult()
            {
                IsValid = false
            });
        }

        var json = file.FileMetadata.AppData.Content;
        var shard = OdinSystemSerializer.Deserialize<PlayerEncryptedShard>(json);

        return (dealer, shard, new ShardVerificationResult()
        {
            Created = shard.Created,
            IsValid = true
        });
    }

    public async Task<DealerShardPackage> GetDealerShardPackage(IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var uid = Guid.Parse(DealerShardConfigUid);
        var options = new ResultOptions
        {
            MaxRecords = 1,
            IncludeHeaderContent = true,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = true,
            IncludeTransferHistory = false
        };

        var existingFile = await fileSystem.Query.GetFileByClientUniqueId(driveId, uid, options: options, odinContext);

        if (null == existingFile)
        {
            return null;
        }

        // var key = odinContext.PermissionsContext.SharedSecretKey;
        // var decryptedKeyHeader = existingFile.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref key);
        // var bytes = decryptedKeyHeader.Decrypt(existingFile.FileMetadata.AppData.Content.FromBase64());

        var bytes = existingFile.FileMetadata.AppData.Content.FromBase64();
        var json = bytes.ToStringFromUtf8Bytes();

        var package = OdinSystemSerializer.Deserialize<DealerShardPackage>(json);
        package.Created = existingFile.FileMetadata.Created;

        return package;
    }

    public async Task<string> DecryptRecoveryKey(SensitiveByteArray distributionKey, IOdinContext odinContext)
    {
        var key = await GetKeyInternalAsync();
        if (key == null)
        {
            throw new OdinClientException("No key found");
        }

        var recoveryKey = key.ShamirDistributionKeyEncryptedRecoveryKey.DecryptKeyClone(distributionKey);

        var text = BIP39Util.GenerateBIP39(recoveryKey.GetKey());
        recoveryKey.Wipe();

        return text;
    }

    private async Task<(IdentityConnectionRegistration, IPeerPasswordRecoveryHttpClient)> CreateClientAsync(OdinId odinId,
        IOdinContext odinContext)
    {
        var icr = await circleNetworkService.GetIcrAsync(odinId, odinContext);
        var authToken = icr.IsConnected() ? icr.CreateClientAuthToken(odinContext.PermissionsContext.GetIcrKey()) : null;
        var httpClient = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerPasswordRecoveryHttpClient>(
            odinId, authToken, FileSystemType.Standard);
        return (icr, httpClient);
    }

    /// <summary>
    /// Creates encrypted shards for the specified players
    /// </summary>
    private Task<(List<DealerShardEnvelope> DealerEnvelops, List<PlayerEncryptedShard> PlayerShards)> CreateShards(
        List<ShamiraPlayer> players,
        int minShards,
        SensitiveByteArray secret,
        IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        var shards = ShamirSecretSharing.GenerateShamirShares(players.Count, minShards, secret.GetKey());

        OdinValidationUtils.AssertIsTrue(players.TrueForAll(p => p.Type == PlayerType.Delegate), "Only Delegate player type is supported");
        OdinValidationUtils.AssertIsTrue(shards.Count == players.Count, "Player and shard count do not match");

        var dealerRecords = new List<DealerShardEnvelope>();
        var playerRecords = new List<PlayerEncryptedShard>();

        // give each player a shard; encrypted with a key
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            var s = shards[i];

            // Dealer encrypts a player's shard with the random key
            var playerEncryptionKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var (iv, cipher) = AesCbc.Encrypt(s.Shard, playerEncryptionKey);


            var playerShardId = Guid.NewGuid();
            playerRecords.Add(new PlayerEncryptedShard()
            {
                Index = s.Index,
                Id = playerShardId,
                Player = player,
                Created = UnixTimeUtc.Now(),
                DealerEncryptedShard = cipher
            });

            dealerRecords.Add(new DealerShardEnvelope()
            {
                Index = i,
                Player = player,
                ShardId = playerShardId,
                EncryptionKey = playerEncryptionKey.GetKey(),
                EncryptionIv = iv
            });
        }

        return Task.FromResult((dealerRecords, playerRecords));
    }

    private async Task SaveDealerPackage(DealerShardPackage envelope, IOdinContext odinContext)
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
    private async Task<Dictionary<OdinId, TransferStatus>> EnqueueShardsForDistribution(
        List<PlayerEncryptedShard> shards,
        IOdinContext odinContext)
    {
        var results = new Dictionary<OdinId, TransferStatus>();

        foreach (PlayerEncryptedShard shard in shards)
        {
            var header = await WritePlayerEncryptedShardToTempDrive(shard, odinContext);
            var transitOptions = new TransitOptions
            {
                IsTransient = true,
                Recipients = [shard.Player.OdinId.DomainName],
                DisableTransferHistory = true,
                UseAppNotification = true,
                AppNotificationOptions = new AppNotificationOptions
                {
                    AppId = default,
                    TypeId = default,
                    TagId = default,
                    Silent = false,
                    PeerSubscriptionId = default,
                    Recipients = null,
                    UnEncryptedMessage = $"{odinContext.Tenant.DomainName} has added you as part of their password recovery process."
                },
                RemoteTargetDrive = SystemDriveConstants.ShardRecoveryDrive,
                Priority = OutboxPriority.High
            };

            var transferStatusMap = await transferService.SendFile(
                header.FileMetadata.File,
                transitOptions,
                TransferFileType.Normal,
                header.ServerMetadata.FileSystemType,
                odinContext);

            var status = transferStatusMap[shard.Player.OdinId];
            results.Add(shard.Player.OdinId, status);
        }

        return results;
    }

    private async Task WriteNewDealerEnvelopeFile(DealerShardPackage dealerShard, IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var file = await fileSystem.Storage.CreateInternalFileId(driveId);

        // var keyHeader = KeyHeader.NewRandom16();
        // var encryptedContent = keyHeader.EncryptDataAes(OdinSystemSerializer.Serialize(dealerShard).ToUtf8ByteArray());
        var keyHeader = KeyHeader.Empty();
        var encryptedContent = OdinSystemSerializer.Serialize(dealerShard).ToUtf8ByteArray();

        var fileMetadata = new FileMetadata(file)
        {
            GlobalTransitId = Guid.NewGuid(),
            AppData = new AppFileMetaData()
            {
                FileType = DealerShardConfigFiletype,
                Content = encryptedContent.ToBase64(),
                UniqueId = Guid.Parse(DealerShardConfigUid)
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

        var serverFileHeader = await fileSystem.Storage.CreateServerFileHeader(file, keyHeader, fileMetadata, serverMetadata, odinContext);
        await fileSystem.Storage.WriteNewFileHeader(file, serverFileHeader, odinContext, raiseEvent: true);
    }

    private async Task OverwriteDealerEnvelopeFile(DealerShardPackage dealerShard, Guid existingFileId,
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
        // var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(driveId);
        // var keyHeader = header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
        // var encryptedContent = keyHeader.EncryptDataAes(OdinSystemSerializer.Serialize(dealerShard).ToUtf8ByteArray());

        var encryptedContent = OdinSystemSerializer.Serialize(dealerShard).ToUtf8ByteArray();

        header.FileMetadata.AppData.Content = encryptedContent.ToBase64();
        await fileSystem.Storage.UpdateActiveFileHeader(file, header, odinContext, raiseEvent: true);
    }

    private async Task<ServerFileHeader> WritePlayerEncryptedShardToTempDrive(PlayerEncryptedShard shard, IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.TransientTempDrive.Alias;
        var file = await fileSystem.Storage.CreateInternalFileId(driveId);

        var keyHeader = KeyHeader.Empty();
        var shardData = OdinSystemSerializer.Serialize(shard);

        var fileMetadata = new FileMetadata(file)
        {
            GlobalTransitId = Guid.NewGuid(),
            AppData = new AppFileMetaData()
            {
                FileType = PlayerEncryptedShardFileType,
                Content = shardData,
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

    /// <summary>
    /// Creates the key we will split and distribute to players
    /// </summary>
    private async Task SaveDistributableKey(SensitiveByteArray distributionKey, DealerShardPackage package, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        // encrypt the recover key w/ distribution key
        var k = await passwordKeyRecoveryService.GetKeyAsync(byPassWaitingPeriod: true, odinContext);
        var recoveryKey = BIP39Util.DecodeBIP39(k.Key);

        var masterKey = odinContext.Caller.GetMasterKey();
        var record = new ShamirKeyRecord()
        {
            //TODO: do we need the master key encrypted as a fallback? I think this will allow us to 
            // add additional players so long as the owner still has their password
            MasterKeyEncryptedShamirDistributionKey = new SymmetricKeyEncryptedAes(secret: masterKey, dataToEncrypt: recoveryKey),
            Created = UnixTimeUtc.Now(),
            ShamirDistributionKeyEncryptedRecoveryKey = new SymmetricKeyEncryptedAes(secret: distributionKey, dataToEncrypt: recoveryKey),
            Players = package.Envelopes.Select(e => e.Player).ToList()
        };

        await Storage.UpsertAsync(tblKeyValue, ShamirRecordStorageId, record);
    }

    private async Task<ShamirKeyRecord> GetKeyInternalAsync()
    {
        var existingKey = await Storage.GetAsync<ShamirKeyRecord>(tblKeyValue, ShamirRecordStorageId);
        return existingKey;
    }
}