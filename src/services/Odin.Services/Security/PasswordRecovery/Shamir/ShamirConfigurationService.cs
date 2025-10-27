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
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
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
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Odin.Services.Util;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

public class ShamirConfigurationService(
    ILogger<ShamirConfigurationService> logger,
    TenantContext tenantContext,
    PasswordKeyRecoveryService passwordKeyRecoveryService,
    PeerOutgoingTransferService transferService,
    IOdinHttpClientFactory odinHttpClientFactory,
    TableKeyValueCached tblKeyValue,
    IdentityDatabase db,
    StandardFileSystem fileSystem,
    IDriveManager driveManager,
    OwnerSecretService secretService,
    OdinConfiguration configuration,
    PeerOutbox peerOutbox,
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
    public const string RotateShardsHasStarted = "Rotate shards has started";
    public static Guid SecurityRiskReportNotificationTypeId { get; } = Guid.Parse("959f197f-4f97-4ff1-b36e-eb237b79eda1");

    public static int CalculateMinAllowedShardCount(int playerCount)
    {
        return playerCount - MinimumMatchingShardsOffset;
    }

    public async Task ConfigureAutomatedRecovery(IOdinContext odinContext)
    {
        AssertCanUseAutomatedRecovery();
        var autoPlayers = configuration.AccountRecovery.AutomatedPasswordRecoveryIdentities?.Select(r => (OdinId)r).ToList() ?? [];

        logger.LogDebug("Configuring automated recovery for auto-players: {players}", string.Join(",", autoPlayers));

        SensitiveByteArray distributionKey = null;
        await using var tx = await db.BeginStackedTransactionAsync();
        tx.AddPostCommitAction(transferService.ProcessOutboxNow);
        try
        {
            var players = autoPlayers.Select(p => new ShamiraPlayer()
            {
                OdinId = p,
                Type = PlayerType.Automatic
            }).ToList();

            (distributionKey, var playerShards) = await ConfigureShardsInternal(players, 3, usesAutomatic: true, odinContext);

            logger.LogDebug("Enqueuing shards for distribution to players.  count: {players}", players.Count);
            var enqueueResults = await this.EnqueueShardsForDistributionForAutomaticIdentities(playerShards, odinContext);
            var failures = enqueueResults.Where(kvp => kvp.Value != TransferStatus.Enqueued).ToList();
            if (failures.Any())
            {
                throw new OdinSystemException($"Failed to enqueue shards for identities [{string.Join(",", failures.Select(f => f.Key))}]");
            }

            await SaveDistributableKey(distributionKey, odinContext);

            tx.Commit();
            logger.LogDebug("Commited shard distribution data");
        }
        catch (OdinClientException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to enqueue shards");
            throw;
        }
        finally
        {
            distributionKey?.Wipe();
        }
    }

    public void AssertCanUseAutomatedRecovery()
    {
        if (!configuration.AccountRecovery.Enabled)
        {
            throw new OdinClientException("Auto-recovery not enabled in configuration");
        }
        
        var autoPlayers = configuration.AccountRecovery.AutomatedPasswordRecoveryIdentities?.Select(r => (OdinId)r).ToList() ?? [];

        if (autoPlayers.Count() < MinimumPlayerCount || configuration.AccountRecovery.AutomatedIdentityKey == Guid.Empty)
        {
            logger.LogWarning("Tried to auto-configure password recovery but too few auto-players are configured.  See configuration " +
                              "Registry::AutomatedPasswordRecoveryIdentities. Minimum is {min}", MinimumPlayerCount);
            // throw new OdinSystemException("Tried to auto-configure password recovery but too few auto-players are configured");
            throw new OdinClientException("Tried to auto-configure password recovery but too few auto-players are configured");
        }
    }

    public async Task ConfigureShards(List<ShamiraPlayer> players, int minShards, IOdinContext odinContext)
    {
        SensitiveByteArray distributionKey = null;
        await using var tx = await db.BeginStackedTransactionAsync();
        tx.AddPostCommitAction(transferService.ProcessOutboxNow);
        try
        {
            (distributionKey, var playerShards) = await ConfigureShardsInternal(players, minShards, usesAutomatic: false, odinContext);

            logger.LogDebug("Enqueuing shards for distribution to players.  count: {players}", players.Count);
            var enqueueResults = await EnqueueShardsForDistribution(playerShards, odinContext);
            var failures = enqueueResults.Where(kvp => kvp.Value != TransferStatus.Enqueued).ToList();
            if (failures.Any())
            {
                throw new OdinClientException($"Failed to enqueue shards for identities [{string.Join(",", failures.Select(f => f.Key))}]");
            }

            await SaveDistributableKey(distributionKey, odinContext);

            tx.Commit();
            logger.LogDebug("Commited shard distribution data");
        }
        catch (OdinClientException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to enqueue shards");
            throw;
        }
        finally
        {
            distributionKey?.Wipe();
        }
    }

    private async Task<(SensitiveByteArray distributionKey, List<PlayerEncryptedShard> PlayerShards)> ConfigureShardsInternal(
        List<ShamiraPlayer> players, int minShards, bool usesAutomatic, IOdinContext odinContext)
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
            Envelopes = shards.DealerEnvelops,
            UsesAutomatedRecovery = usesAutomatic
        };

        await SaveDealerPackage(package, odinContext);
        return (distributionKey, shards.PlayerShards);
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
                var result = response.Content;
                logger.LogDebug("Shard verification call succeed for identity: {identity}.  Result " +
                                "was: IsValid:{isValid}.  remoteServerError: {remoteError}", player,
                    result.IsValid,
                    result.RemoteServerError);

                return result;
            }

            logger.LogDebug("Shard verification call failed for identity: {identity}.  Http Status code: {code}", player,
                response.StatusCode);
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
        logger.LogDebug("Verifying dealer shard {shardId}", shardId);
        try
        {
            var shardDrive = await driveManager.GetDriveAsync(SystemDriveConstants.ShardRecoveryDrive.Alias);

            if (null == shardDrive)
            {
                logger.LogDebug("Could not perform shard verification; Sharding drive not " +
                                "yet configured (Tenant probably needs to upgrade)");

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

            if (isValid)
            {
                var lastSeen = await lastSeenService.GetLastSeenAsync(odinContext.Tenant);
                var trustLevel = ShardTrustLevel.Critical; // default = worst case

                if (shard.Player.Type == PlayerType.Automatic)
                {
                    trustLevel = ShardTrustLevel.High;
                }
                else
                {
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
                    }
                }

                return new ShardVerificationResult
                {
                    RemoteServerError = false,
                    IsValid = true,
                    TrustLevel = trustLevel,
                    Created = shard?.Created ?? 0
                };
            }

            // not valid - add some logging to see what's up
            if (shard == null)
            {
                logger.LogDebug("Dealer shard with id: {shardId} is null", shardId);
            }

            if (sender != odinContext.Caller.OdinId.GetValueOrDefault())
            {
                logger.LogDebug("Dealer shard with id: {shardId} has mismatching caller and sender", shardId);
            }

            return new ShardVerificationResult
            {
                RemoteServerError = false,
                IsValid = false,
                TrustLevel = ShardTrustLevel.Critical,
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
        odinContext.Caller.AssertHasMasterKey();

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
            Updated = package.Updated,
            UsesAutomaticRecovery = package.UsesAutomatedRecovery
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
            logger.LogDebug(
                "Shard drive not yet configured (Tenant probably needs to upgrade).  So GetDealerShardPackage will return null.");
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

    /// <summary>
    /// Rotates keys in the shares using all existing players
    /// </summary>
    public async Task RotateShardKeysIfNeeded(IOdinContext odinContext)
    {
        try
        {
            var package = await this.GetDealerShardPackage(odinContext);
            if (null == package)
            {
                return;
            }

            var passwordLastUpdated = await secretService.GetPasswordLastUpdated();
            if (passwordLastUpdated == null)
            {
                // password never changed
                return;
            }

            // if the package was updated before the password was changed, we need to rotate it
            if (package.Updated < passwordLastUpdated.Value)
            {
                logger.LogDebug(RotateShardsHasStarted);
                var players = package.Envelopes.Select(e => e.Player).ToList();
                await this.ConfigureShards(players, package.MinMatchingShards, odinContext);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to start shard rotation shards");
        }
    }

    /// <summary>
    /// Creates a context for writing shards to the automated identity
    /// </summary>
    public Task<IOdinContext> GetDotYouContextAsync(OdinId callerOdinId, ClientAuthenticationToken token)
    {
        if (!configuration.AccountRecovery.Enabled)
        {
            throw new OdinSystemException("Automated recovery is disabled in config");
        }
        
        if (!configuration.AccountRecovery.AutomatedPasswordRecoveryIdentities.Any())
        {
            throw new OdinSystemException("No Automated identities are configured");
        }

        if (configuration.AccountRecovery.AutomatedPasswordRecoveryIdentities.All(ident => ident != tenantContext.HostOdinId))
        {
            throw new OdinSecurityException("Not an automated identity");
        }

        var key = configuration.AccountRecovery.AutomatedIdentityKey;
        if (!ByteArrayUtil.EquiByteArrayCompare(token.AccessTokenHalfKey.GetKey(), key.ToByteArray()))
        {
            throw new OdinSecurityException("Invalid token");
        }

        var dotYouContext = new OdinContext();
        var drive = SystemDriveConstants.ShardRecoveryDrive;
        var driveGrants = new List<DriveGrant>()
        {
            new DriveGrant
            {
                DriveId = drive.Alias,
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = drive,
                    Permission = DrivePermission.Write
                },
            }
        };

        var permissionGroups = new Dictionary<string, PermissionGroup>()
        {
            { "automated-shard-permissions", new PermissionGroup(new PermissionSet(), driveGrants, null, null) }
        };

        var permissionContext = new PermissionContext(permissionGroups, sharedSecretKey: Guid.Empty.ToByteArray().ToSensitiveByteArray());
        var callerContext = new CallerContext(
            odinId: callerOdinId,
            masterKey: null,
            securityLevel: SecurityGroupType.Authenticated,
            circleIds: null,
            tokenType: token.ClientTokenType);

        dotYouContext.Caller = callerContext;
        dotYouContext.SetPermissionContext(permissionContext);

        return Task.FromResult<IOdinContext>(dotYouContext);
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


    private async Task<Dictionary<OdinId, TransferStatus>> EnqueueShardsForDistributionForAutomaticIdentities(
        List<PlayerEncryptedShard> shards, IOdinContext odinContext)
    {
        var status = new Dictionary<OdinId, TransferStatus>();
        foreach (PlayerEncryptedShard shard in shards)
        {
            var recipient = shard.Player.OdinId;

            var header = await WritePlayerEncryptedShardToTempDrive(shard, odinContext);
            var options = new TransitOptions
            {
                IsTransient = true,
                Recipients = [recipient],
                DisableTransferHistory = true,
                UseAppNotification = false,
                RemoteTargetDrive = SystemDriveConstants.ShardRecoveryDrive,
                Priority = OutboxPriority.High
            };

            try
            {
                var clientAuthToken = new ClientAccessToken()
                {
                    Id = configuration.AccountRecovery.AutomatedIdentityKey,
                    AccessTokenHalfKey = configuration.AccountRecovery.AutomatedIdentityKey.ToByteArray().ToSensitiveByteArray(),
                    ClientTokenType = ClientTokenType.AutomatedPasswordRecovery,
                    SharedSecret = Guid.Empty.ToByteArray().ToSensitiveByteArray(),
                };

                var item = new OutboxFileItem()
                {
                    Priority = 100,
                    Type = OutboxItemType.File,
                    File = header.FileMetadata.File,
                    Recipient = recipient,
                    DependencyFileId = options.OutboxDependencyFileId,
                    State = new OutboxItemState()
                    {
                        IsTransientFile = true,
                        Attempts = { },
                        OriginalTransitOptions = options,
                        EncryptedClientAuthToken = clientAuthToken.ToAuthenticationToken().ToPortableBytes(),
                        TransferInstructionSet = CreateTransferInstructionSet(
                            KeyHeader.Empty(),
                            clientAuthToken,
                            SystemDriveConstants.ShardRecoveryDrive,
                            TransferFileType.Normal,
                            FileSystemType.Standard,
                            options),
                        Data = [],
                        DataSourceOverride = default
                    }
                };
                await peerOutbox.AddItemAsync(item);
                status.Add(recipient, TransferStatus.Enqueued);
            }
            catch (Exception ex)
            {
                logger.LogError("Failed while creating outbox item {msg}", ex.Message);
                status.Add(recipient, TransferStatus.EnqueuedFailed);
            }
        }

        return status;
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
        var k = await passwordKeyRecoveryService.GetRecoveryKeyAsync(byPassWaitingPeriod: true, odinContext);
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

    private EncryptedRecipientTransferInstructionSet CreateTransferInstructionSet(KeyHeader keyHeaderToBeEncrypted,
        ClientAccessToken clientAccessToken,
        TargetDrive targetDrive,
        TransferFileType transferFileType,
        FileSystemType fileSystemType, TransitOptions transitOptions)
    {
        var sharedSecret = clientAccessToken.SharedSecret;
        var iv = ByteArrayUtil.GetRndByteArray(16);
        var sharedSecretEncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeaderToBeEncrypted, iv, ref sharedSecret);

        return new EncryptedRecipientTransferInstructionSet()
        {
            TargetDrive = targetDrive,
            TransferFileType = transferFileType,
            FileSystemType = fileSystemType,
            SharedSecretEncryptedKeyHeader = sharedSecretEncryptedKeyHeader,
        };
    }
}