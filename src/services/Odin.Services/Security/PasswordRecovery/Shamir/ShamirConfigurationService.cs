using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;
using Odin.Services.LastSeen;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Odin.Services.Util;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

public class ShamirConfigurationService(
    ILogger<ShamirConfigurationService> logger,
    PasswordKeyRecoveryService passwordKeyRecoveryService,
    PeerOutgoingTransferService transferService,
    IOdinHttpClientFactory odinHttpClientFactory,
    TableKeyValueCached tblKeyValue,
    IdentityDatabase db,
    StandardFileSystem fileSystem,
    IDriveManager driveManager,
    ILastSeenService lastSeenService)
{
    private const int DealerShardConfigFiletype = 44532;
    private const int PlayerEncryptedShardFileType = 74829;
    private const string DealerShardConfigUid = "88be2b93-a5af-4884-adff-c73b2c9b04d4";

    private static readonly Guid ShamirRecordStorageId = Guid.Parse("39c98971-ac39-4b4b-8eee-68a1742203c6");
    private const string ContextKey = "078e018e-e6b3-4349-b635-721b43d35241";
    private static readonly SingleKeyValueStorage Storage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ContextKey));

    public const int MinimumPlayerCount = 3;
    public const int MinimumMatchingShardsOffset = 1;

    public static int CalculateMinAllowedShardCount(int playerCount)
    {
        return playerCount - MinimumMatchingShardsOffset;
    }

    public async Task ConfigureShards(List<ShamiraPlayer> players, int minShards, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(players, nameof(players));
        OdinValidationUtils.AssertIsTrue(players.Count >= MinimumPlayerCount,
            $"You need at least {MinimumPlayerCount} trusted connections");
        OdinValidationUtils.AssertIsTrue(players.Count >= minShards, "The number of players must be greater than min shards");

        var minAllowedShards = CalculateMinAllowedShardCount(players.Count);
        OdinValidationUtils.AssertIsTrue(minShards >= minAllowedShards, "The minimum number of matching shards must be " +
                                                                        $"at least {minAllowedShards} since you have {players.Count} " +
                                                                        $"trusted connections selected");

        OdinValidationUtils.AssertValidRecipientList(players.Select(p => p.OdinId), false, odinContext.Tenant);

        var hashedRecoveryEmail = await passwordKeyRecoveryService.GetHashedRecoveryEmail();
        var distributionKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
        var shards = await CreateShards(players, minShards, distributionKey, hashedRecoveryEmail, odinContext);

        var package = new DealerShardPackage
        {
            MinMatchingShards = minShards,
            Envelopes = shards.DealerEnvelops
        };

        await using var tx = await db.BeginStackedTransactionAsync();
        tx.AddPostCommitAction(transferService.ProcessOutboxNow);
        await SaveDealerPackage(package, odinContext);

        logger.LogDebug("Enqueuing shards for distribution to players.  count: {players}", players.Count);
        var enqueueResults = await EnqueueShardsForDistribution(shards.PlayerShards, odinContext);
        var failures = enqueueResults.Where(kvp => kvp.Value != TransferStatus.Enqueued).ToList();
        if (failures.Any())
        {
            throw new OdinClientException($"Failed to enqueue shards for identities [{string.Join(",", failures.Select(f => f.Key))}]");
        }

        await SaveDistributableKey(distributionKey, odinContext);

        tx.Commit();
        logger.LogDebug("Commited shard distribution data");

        distributionKey.Wipe();
    }

    /// <summary>
    /// Verifies shards held by players
    /// </summary>
    public async Task<RemoteShardVerificationResult> VerifyRemotePlayerShards(IOdinContext odinContext)
    {
        // get the preconfigured package
        var package = await this.GetDealerShardPackage(odinContext);

        if (package == null)
        {
            logger.LogDebug("Sharding for dealer {d} not configured.", odinContext.Caller);
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
        //todo: change to generic file system call
        try
        {
            var client = CreateClientAsync(player, odinContext);
            var response = await client.VerifyShard(new VerifyShardRequest()
            {
                ShardId = shardId
            });

            if (response.IsSuccessStatusCode)
            {
                return response.Content;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed during shard verification for identity: {identity}", player);
        }

        return new ShardVerificationResult
        {
            RemoteServerError = true,
            IsValid = false,
            Created = UnixTimeUtc.Now(),
            TrustLevel = ShardTrustLevel.Critical
        };
    }

    /// <summary>
    /// Verifies the shard given to this identity from a dealer
    /// </summary>
    public async Task<ShardVerificationResult> VerifyDealerShard(
        Guid shardId,
        IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsAuthenticated();
        try
        {
            var shardDrive = await driveManager.GetDriveAsync(SystemDriveConstants.ShardRecoveryDrive.Alias);
            
            if (null == shardDrive)
            {
                logger.LogDebug("Could not perform shard verification; Sharding drive not yet configured (Tenant probably needs to upgrade)");
                return new ShardVerificationResult
                {
                    RemoteServerError = true,
                    IsValid = false,
                    TrustLevel = ShardTrustLevel.Critical,
                    Created = UnixTimeUtc.Now()
                };
            }

            var (shard, sender) = await GetShardStoredForDealer(shardId, odinContext);
            var isValid = shard != null && sender == odinContext.Caller.OdinId.GetValueOrDefault();

            var lastSeen = await lastSeenService.GetLastSeenAsync(odinContext.Tenant);
            var trustLevel = ShardTrustLevel.Critical; // default = worst case

            if (lastSeen.HasValue)
            {
                var now = DateTime.UtcNow;
                var elapsed = now - lastSeen.Value.ToDateTime();

                if (elapsed < TimeSpan.FromDays(14))
                {
                    trustLevel = ShardTrustLevel.High;
                }
                else if (elapsed < TimeSpan.FromDays(30))
                {
                    trustLevel = ShardTrustLevel.Low;
                }
                else if (elapsed < TimeSpan.FromDays(90))
                {
                    trustLevel = ShardTrustLevel.Medium;
                }
                // otherwise remains Critical
            }

            return new ShardVerificationResult
            {
                RemoteServerError = false,
                IsValid = isValid,
                TrustLevel = trustLevel,
                Created = shard?.Created ?? 0
            };
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not perform shard verification");
            // if anything fails, just tell the caller this
            // server is not capable of sharding right now
            return new ShardVerificationResult
            {
                RemoteServerError = true,
                IsValid = false,
                TrustLevel = ShardTrustLevel.Critical,
                Created = UnixTimeUtc.Now()
            };
        }
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
            Updated = package.Updated
        };
    }

    /// <summary>
    /// Gets the shard a player has stored for a dealer.  i.e. this is the info that will be returned
    /// when a dealer requests to reset their password
    /// </summary>
    public async Task<(PlayerEncryptedShard shard, OdinId? sender)> GetShardStoredForDealer(
        Guid shardId,
        IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;

        var options = new ResultOptions
        {
            MaxRecords = 1,
            IncludeHeaderContent = true,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = false,
            IncludeTransferHistory = false
        };

        var byPassAclCheckContext = OdinContextUpgrades.UpgradeToByPassAclCheck(
            SystemDriveConstants.ShardRecoveryDrive,
            DrivePermission.Read,
            odinContext);

        var file = await fileSystem.Query.GetFileByClientUniqueId(driveId, shardId, options, byPassAclCheckContext);

        if (file == null)
        {
            return (null, null);
        }

        var shard = PlayerEncryptedShard.Deserialize(file.FileMetadata.AppData.Content);
        OdinId? sender = string.IsNullOrEmpty(file.FileMetadata.SenderOdinId) ? null : (OdinId)file.FileMetadata.SenderOdinId;
        return (shard, sender);
    }

    public async Task<DealerShardPackage> GetDealerShardPackage(IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
        var shardDrive = await driveManager.GetDriveAsync(driveId);
            
        if (null == shardDrive)
        {
            logger.LogDebug("Shard drive not yet configured (Tenant probably needs to upgrade).  So GetDealerShardPackage will return null.");
            return null;
        }
        
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

        var json = existingFile.FileMetadata.AppData.Content;
        var package = DealerShardPackage.Deserialize(json);
        package.Updated = existingFile.FileMetadata.Updated;

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

    private IPeerPasswordRecoveryHttpClient CreateClientAsync(OdinId odinId, IOdinContext odinContext)
    {
        // var icr = await circleNetworkService.GetIcrAsync(odinId, odinContext);
        // var authToken = icr.IsConnected() ? icr.CreateClientAuthToken(odinContext.PermissionsContext.GetIcrKey()) : null;
        // var httpClient = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerPasswordRecoveryHttpClient>(
        //     odinId, authToken, FileSystemType.Standard);
        // return (icr, httpClient);

        var httpClient = odinHttpClientFactory.CreateClient<IPeerPasswordRecoveryHttpClient>(odinId, FileSystemType.Standard);
        return httpClient;
    }

    /// <summary>
    /// Creates encrypted shards for the specified players
    /// </summary>
    private Task<(List<DealerShardEnvelope> DealerEnvelops, List<PlayerEncryptedShard> PlayerShards)> CreateShards(
        List<ShamiraPlayer> players,
        int minShards,
        SensitiveByteArray secret,
        Guid recoveryEmailHash,
        IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        var shards = ShamirSecretSharing.GenerateShamirShares(players.Count, minShards, secret.GetKey());
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
                RecoveryEmailHash = recoveryEmailHash,
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

    /// <summary>
    /// Saves the dealer information for the encrypted shards
    /// </summary>
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
                    AppId = SystemAppConstants.OwnerAppId,
                    TypeId = OwnerAppConstants.PasswordRecoveryRecruitedTypeId,
                    TagId = default,
                    Silent = false,
                    PeerSubscriptionId = default,
                    Recipients = null,
                    UnEncryptedMessage = $"{odinContext.Tenant.DomainName} has added you as part of their password recovery process. "
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

        var keyHeader = KeyHeader.Empty();

        var fileMetadata = new FileMetadata(file)
        {
            GlobalTransitId = Guid.NewGuid(),
            AppData = new AppFileMetaData()
            {
                FileType = DealerShardConfigFiletype,
                Content = DealerShardPackage.Serialize(dealerShard),
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
        header.FileMetadata.AppData.Content = DealerShardPackage.Serialize(dealerShard);
        header.FileMetadata.IsEncrypted = false;
        await fileSystem.Storage.UpdateActiveFileHeader(file, header, odinContext, raiseEvent: true);
    }

    private async Task<ServerFileHeader> WritePlayerEncryptedShardToTempDrive(PlayerEncryptedShard shard, IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.TransientTempDrive.Alias;
        var file = await fileSystem.Storage.CreateInternalFileId(driveId);

        var keyHeader = KeyHeader.Empty();

        var fileMetadata = new FileMetadata(file)
        {
            GlobalTransitId = Guid.NewGuid(),
            AppData = new AppFileMetaData()
            {
                FileType = PlayerEncryptedShardFileType,
                Content = PlayerEncryptedShard.Serialize(shard),
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
    /// Saves the key used to decrypt the <see cref="PlayerEncryptedShard"/>s
    /// </summary>
    private async Task SaveDistributableKey(SensitiveByteArray distributionKey, IOdinContext odinContext)
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
        };

        var n = await Storage.UpsertAsync(tblKeyValue, ShamirRecordStorageId, record);
        if (n != 1)
        {
            throw new OdinSystemException($"Failed to save key. {n} records affected");
        }
    }

    private async Task<ShamirKeyRecord> GetKeyInternalAsync()
    {
        var existingKey = await Storage.GetAsync<ShamirKeyRecord>(tblKeyValue, ShamirRecordStorageId);
        return existingKey;
    }
}