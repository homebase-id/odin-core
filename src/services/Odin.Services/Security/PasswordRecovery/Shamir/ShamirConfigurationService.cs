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
using Odin.Core.Storage.Database.Identity.Wrappers;
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
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;
using Odin.Services.LastSeen;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Apps.Builtin;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Odin.Services.Security.ZeroKnowledge;
using Odin.Services.Util;
using PeerDriveQueryService = Odin.Services.Peer.Outgoing.Drive.Query.PeerDriveQueryService;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

public class ShamirConfigurationService(
    ILogger<ShamirConfigurationService> logger,
    TenantContext tenantContext,
    PasswordKeyRecoveryService passwordKeyRecoveryService,
    PeerOutgoingTransferService transferService,
    TableKeyValueCached tblKeyValue,
    IdentityDatabase db,
    StandardFileSystem fileSystem,
    IDriveManager driveManager,
    OwnerSecretService secretService,
    OdinConfiguration configuration,
    PeerOutbox peerOutbox,
    ShardRotationGate rotationGate,
    ZeroKnowledgeKeyService zeroKnowledgeKeyService,
    PeerDriveQueryService peerDriveQueryService,
    ILastSeenService lastSeenService) : ShamirBaseService<ShamirConfigurationService>(logger, fileSystem, driveManager, lastSeenService)
{
    private const int DealerShardConfigFiletype = 44532;
    private const int PlayerEncryptedShardFileType = 74829;
    
    private static readonly Guid ShamirRecordStorageId = Guid.Parse("39c98971-ac39-4b4b-8eee-68a1742203c6");
    private const string ContextKey = "078e018e-e6b3-4349-b635-721b43d35241";
    private static readonly SingleKeyValueStorage Storage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ContextKey));

    public const int MinimumPlayerCount = 3;

    /// <summary>
    /// The length of the key split into shards; recovery reconstructs it to this length (#1736).
    /// </summary>
    public const int DistributionKeyLength = 16;
    public const int MinimumMatchingShardsOffset = 1;
    public const string RotateShardsHasStarted = "Rotate shards has started";
    public static Guid SecurityRiskReportNotificationTypeId { get; } = Guid.Parse("959f197f-4f97-4ff1-b36e-eb237b79eda1");

    public static int CalculateMinAllowedShardCount(int playerCount)
    {
        return playerCount - MinimumMatchingShardsOffset;
    }

    public async Task ConfigureAutomatedRecovery(IOdinContext odinContext)
    {
        var players = configuration.AccountRecovery.AutomatedPasswordRecoveryIdentities?.Select(p => new ShamiraPlayer()
        {
            OdinId = (OdinId)p,
            Type = PlayerType.Automatic
        }).ToList() ?? [];

        Logger.LogDebug("Configuring automated recovery for auto-players: {players}", string.Join(",", players.Select(p => p.OdinId)));

        await DistributeShards(players, 3, odinContext);
    }

    public void AssertCanUseAutomatedRecovery()
    {
        if (!configuration.AccountRecovery.Enabled)
        {
            throw new OdinClientException("Auto-recovery not enabled in configuration");
        }

        if (!HasAutomatedPlayers())
        {
            Logger.LogWarning("Tried to auto-configure password recovery but too few auto-players are configured.  See configuration " +
                              "Registry::AutomatedPasswordRecoveryIdentities. Minimum is {min}", MinimumPlayerCount);
            // throw new OdinSystemException("Tried to auto-configure password recovery but too few auto-players are configured");
            throw new OdinClientException("Tried to auto-configure password recovery but too few auto-players are configured");
        }
    }

    private bool CanUseAutomatedRecovery() => configuration.AccountRecovery.Enabled && HasAutomatedPlayers();

    private bool HasAutomatedPlayers() =>
        (configuration.AccountRecovery.AutomatedPasswordRecoveryIdentities?.Count ?? 0) >= MinimumPlayerCount &&
        configuration.AccountRecovery.AutomatedIdentityKey != Guid.Empty;

    /// <summary>
    /// A friend deal, dealt in the browser (<see cref="ConfigureShardsRequest"/>).  Replaces any deal,
    /// automated or not.  Each gated friend must hold their Shard Recovery drive's keys only in their
    /// browser, and their shard must be sealed to that drive's current key: anything else would let
    /// their server release it without them.
    /// </summary>
    public async Task ConfigureShards(ConfigureShardsRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        request.Validate();
        OdinValidationUtils.AssertValidRecipientList(request.Shards.Select(s => s.Player.OdinId), false, odinContext.Tenant);
        await zeroKnowledgeKeyService.AssertKeysExistAsync();

        foreach (var shard in request.Shards.Where(s => s.IsGated))
        {
            await AssertSealedToHolderAsync(shard, odinContext);
        }

        var hashedRecoveryEmail = await passwordKeyRecoveryService.GetHashedRecoveryEmail();

        await using var tx = await db.BeginStackedTransactionAsync();
        tx.AddPostCommitAction(transferService.ProcessOutboxNow);

        await SaveDealerPackage(new DealerShardPackage
        {
            MinMatchingShards = request.MinMatchingShards,
            Envelopes = request.Shards.Select(s => new DealerShardEnvelope
            {
                Player = s.Player,
                ShardId = s.ShardId,
                Index = s.Index,
                EncryptionKey = s.EncryptionKey,
                EncryptionIv = s.EncryptionIv
            }).ToList(),
            UsesAutomatedRecovery = false
        }, odinContext);

        var failures = new List<OdinId>();
        foreach (var shard in request.Shards)
        {
            var file = shard.IsGated
                ? await WriteShardToTempDriveAsync(shard.ShardId, shard.DepositContent, new SealedKeyHeader(shard.DepositKeyHeader),
                    odinContext, hashedRecoveryEmail)
                : await WritePlayerEncryptedShardToTempDrive(new PlayerEncryptedShard
                {
                    Id = shard.ShardId,
                    Index = shard.Index,
                    RecoveryEmailHash = hashedRecoveryEmail,
                    Player = shard.Player,
                    Created = UnixTimeUtc.Now(),
                    DealerEncryptedShard = shard.DealerEncryptedShard
                }, odinContext);

            if (await SendShardAsync(shard.Player.OdinId, file, odinContext) != TransferStatus.Enqueued)
            {
                failures.Add(shard.Player.OdinId);
            }
        }

        if (failures.Any())
        {
            throw new OdinClientException($"Failed to enqueue shards for identities [{string.Join(",", failures)}]");
        }

        // A friend deal locks no master key here: the automated deal's copy goes, and the friends'
        // secret locks the zero-knowledge master key instead.
        await Storage.DeleteAsync(tblKeyValue, ShamirRecordStorageId);
        await zeroKnowledgeKeyService.SaveShamirCopiesAsync(request.ShamirCopies, odinContext);

        tx.Commit();
        Logger.LogInformation("Friend deal saved: {count} shards, {min} needed", request.Shards.Count, request.MinMatchingShards);
    }

    /// <summary>
    /// Refuses a gated shard unless its holder's Shard Recovery drive is zero knowledge (their server
    /// can't open the write-only private key) and it is sealed to that drive's current key.
    /// </summary>
    private async Task AssertSealedToHolderAsync(DealtShard shard, IOdinContext odinContext)
    {
        var holder = shard.Player.OdinId;
        var key = await peerDriveQueryService.GetRemoteDriveWriteOnlyPublicKeyAsync(holder, RecoveryAppSlug,
            BuiltinDrives.ShardRecoveryDrive.DriveSlug, odinContext);

        if (!key.IsZeroKnowledge)
        {
            throw new OdinClientException(
                $"{holder} hasn't logged in since their recovery drive became zero knowledge; they can't hold a gated shard yet",
                OdinClientErrorCode.ShardHolderNotZeroKnowledge);
        }

        if (!ZkSeal.IsSealedTo(shard.DepositKeyHeader.EncryptedAesKey, key.PublicKeyJwk))
        {
            throw new OdinClientException($"The shard for {holder} is not sealed to their current key",
                OdinClientErrorCode.InvalidKeyHeader);
        }
    }

    private static readonly OdinSlug RecoveryAppSlug =
        BuiltinApps.All.Single(a => a.AppId == SystemAppConstants.RecoveryAppId).AppSlug;

    /// <summary>
    /// Deals a fresh set of shards to the automated identities <paramref name="players"/> and sends
    /// them, reached with the configured AutomatedIdentityKey.  Only the automated deal is dealt here:
    /// friend deals are dealt in the browser (<see cref="ConfigureShards"/>).
    /// </summary>
    private async Task DistributeShards(List<ShamiraPlayer> players, int minShards, IOdinContext odinContext,
        bool movingOffThePhrase = false)
    {
        AssertCanUseAutomatedRecovery();

        SensitiveByteArray distributionKey = null;
        await using var tx = await db.BeginStackedTransactionAsync();
        tx.AddPostCommitAction(transferService.ProcessOutboxNow);
        try
        {
            (distributionKey, var playerShards) = await ConfigureShardsInternal(players, minShards, odinContext);

            Logger.LogDebug("Enqueuing shards for distribution to players.  count: {players}", players.Count);
            var enqueueResults = await EnqueueShardsForDistributionForAutomaticIdentities(playerShards, odinContext);
            var failures = enqueueResults.Where(kvp => kvp.Value != TransferStatus.Enqueued).ToList();
            if (failures.Any())
            {
                throw new OdinSystemException(
                    $"Failed to enqueue shards for identities [{string.Join(",", failures.Select(f => f.Key))}]");
            }

            await SaveDistributableKey(distributionKey, odinContext, movingOffThePhrase);

            // The automated deal replaces a friend deal, whose copies can then recover nothing
            await zeroKnowledgeKeyService.ForgetShamirCopiesAsync();

            tx.Commit();
            Logger.LogDebug("Commited shard distribution data");
        }
        catch (OdinClientException)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to enqueue shards");
            throw;
        }
        finally
        {
            distributionKey?.Wipe();
        }
    }

    private async Task<(SensitiveByteArray distributionKey, List<PlayerEncryptedShard> PlayerShards)> ConfigureShardsInternal(
        List<ShamiraPlayer> players, int minShards, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(players, nameof(players));
        OdinValidationUtils.AssertIsTrue(players.Count >= MinimumPlayerCount,
            $"You need at least {MinimumPlayerCount} trusted connections");
        OdinValidationUtils.AssertIsTrue(players.Count >= minShards, "The number of players must be greater than min shards");

        // var minAllowedShards = CalculateMinAllowedShardCount(players.Count);
        // OdinValidationUtils.AssertIsTrue(minShards >= minAllowedShards, "The minimum number of matching shards must be " +
        //                                                                 $"at least {minAllowedShards} since you have {players.Count} " +
        //                                                                 $"trusted connections selected");

        OdinValidationUtils.AssertValidRecipientList(players.Select(p => p.OdinId), false, odinContext.Tenant);

        var hashedRecoveryEmail = await passwordKeyRecoveryService.GetHashedRecoveryEmail();
        var distributionKey = ByteArrayUtil.GetRndByteArray(DistributionKeyLength).ToSensitiveByteArray();
        var shards = await CreateShards(players, minShards, distributionKey, hashedRecoveryEmail, odinContext);

        var package = new DealerShardPackage
        {
            MinMatchingShards = minShards,
            Envelopes = shards.DealerEnvelops,
            UsesAutomatedRecovery = true
        };

        await SaveDealerPackage(package, odinContext);
        return (distributionKey, shards.PlayerShards);
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

    /// <summary>The master key, from the distribution key the returned shards rebuilt.</summary>
    public async Task<SensitiveByteArray> DecryptMasterKeyAsync(SensitiveByteArray distributionKey)
    {
        var key = await GetKeyInternalAsync();
        if (key == null)
        {
            throw new OdinClientException("No key found");
        }

        if (key.ShamirDistributionKeyEncryptedMasterKey != null)
        {
            return key.ShamirDistributionKeyEncryptedMasterKey.DecryptKeyClone(distributionKey);
        }

        // A deal from before recovery in the browser locks the phrase, which still opens the master
        // key until the takeover re-deals it.
        using var recoveryKey = key.ShamirDistributionKeyEncryptedRecoveryKey.DecryptKeyClone(distributionKey);
        return await passwordKeyRecoveryService.AssertValidKeyAsync(BIP39Util.GenerateBIP39(recoveryKey.GetKey()));
    }

    /// <summary>
    /// For the phrase takeover, before the phrase is handed over: a deal from before recovery in the
    /// browser locks the phrase, which the server is about to forget, so it's dealt again to the same
    /// players with the same threshold.  Harmless if the takeover then never happens: a deal locking
    /// the master key is how every deal works now.  Call it outside any transaction: a failed deal
    /// must roll back on its own.
    /// </summary>
    /// <remarks>
    /// A deal that can't be repeated (a friend no longer connected, automated recovery off on this
    /// host, no dealer package), checked before dealing, is removed rather than kept: without the
    /// phrase it could never recover anything, and keeping it would block the takeover at every
    /// login.  The security page then shows recovery as not set up
    /// (<c>OwnerSecurityHealthService.GetSecurityNeedsAttentionStatus</c>).  Any other failure throws,
    /// the phrase is not handed over, and the next login tries again.  Shares
    /// <see cref="RotateShardKeysIfNeeded"/>'s gate, so the two never deal at once, but waits for it
    /// and ignores its cooldown: a rotation that keeps failing must not block this.
    /// </remarks>
    internal async Task MoveOffThePhraseAsync(IOdinContext odinContext)
    {
        if (!await DealLocksThePhraseAsync())
        {
            return;
        }

        if (!await rotationGate.WaitToEnterAsync(MoveOffThePhraseWait))
        {
            throw new OdinClientException("Recovery shards are being dealt; try again later");
        }

        try
        {
            // Again inside the gate: a rotation that just finished may have dealt it again already
            if (!await DealLocksThePhraseAsync())
            {
                return;
            }

            var package = await GetDealerShardPackage(odinContext);
            var cannotRepeat = package == null ? "no dealer package" : WhyTheDealCannotBeRepeated(package);
            if (cannotRepeat == null)
            {
                await DealAgainAsync(package, odinContext, movingOffThePhrase: true);
                return;
            }

            Logger.LogWarning("The recovery shards can't be dealt again for the phrase takeover ({reason}); removing the old deal",
                cannotRepeat);
            await RemoveDealAsync(odinContext);
        }
        finally
        {
            rotationGate.Exit();
        }
    }

    /// <summary>Null when <paramref name="package"/> can be dealt again; otherwise the permanent reason why not.</summary>
    private string WhyTheDealCannotBeRepeated(DealerShardPackage package)
    {
        if (!package.UsesAutomatedRecovery)
        {
            return "friend deals are dealt in the browser now";
        }

        return CanUseAutomatedRecovery() ? null : "automated recovery is off on this host";
    }

    private static readonly TimeSpan MoveOffThePhraseWait = TimeSpan.FromSeconds(5);

    private async Task<bool> DealLocksThePhraseAsync()
    {
        var key = await GetKeyInternalAsync();
        return key != null && key.ShamirDistributionKeyEncryptedMasterKey == null;
    }

    private async Task RemoveDealAsync(IOdinContext odinContext)
    {
        await using var tx = await db.BeginStackedTransactionAsync();
        var file = await GetDealerFileForWritingAsync(odinContext);
        if (file != null)
        {
            await FileSystem.Storage.HardDeleteLongTermFile(
                new InternalDriveFileId(WellKnownAppDrives.ShardRecoveryDrive.Alias, file.FileId), odinContext);
        }

        await Storage.DeleteAsync(tblKeyValue, ShamirRecordStorageId);
        tx.Commit();
    }

    /// <summary>The automated deal again: same players and threshold; only the shards are new.</summary>
    private async Task DealAgainAsync(DealerShardPackage package, IOdinContext odinContext, bool movingOffThePhrase)
    {
        var players = package.Envelopes.Select(e => e.Player).ToList();
        await DistributeShards(players, package.MinMatchingShards, odinContext, movingOffThePhrase);
    }

    /// <summary>
    /// True when the automated deal <paramref name="package"/> was dealt before the password last
    /// changed.  Never for a friend deal: a password change doesn't change the friends' secret
    /// (docs/zero-knowledge-recovery-plan.md, decision 2).
    /// </summary>
    public async Task<bool> IsRotationPending(DealerShardPackage package)
    {
        if (package?.UsesAutomatedRecovery != true)
        {
            return false;
        }

        var passwordLastUpdated = await secretService.GetPasswordLastUpdated();
        return passwordLastUpdated != null && package.Updated < passwordLastUpdated.Value;
    }

    /// <summary>
    /// Runs on owner requests: deals the automated deal again after a password change, and removes a
    /// friend deal that can recover nothing.
    /// </summary>
    /// <remarks>
    /// A friend deal without its zero-knowledge copies was dealt by the server, before recovery in the
    /// browser (its gated shards aren't sealed to the friends, F4), or a reset with today's master key
    /// forgot its copies.  Removing it turns on the security page's red dot, and the owner deals again.
    /// </remarks>
    public async Task RotateShardKeysIfNeeded(IOdinContext odinContext)
    {
        if (!rotationGate.TryEnter())
        {
            return;
        }

        try
        {
            var package = await this.GetDealerShardPackage(odinContext);
            if (package is { UsesAutomatedRecovery: false } && !await zeroKnowledgeKeyService.HasShamirCopiesAsync())
            {
                Logger.LogInformation("Removing a friend deal that can recover nothing (no zero-knowledge copies)");
                await RemoveDealAsync(odinContext);
            }
            else if (await IsRotationPending(package))
            {
                Logger.LogDebug(RotateShardsHasStarted);
                await DealAgainAsync(package, odinContext, movingOffThePhrase: false);
            }
        }
        catch (Exception e)
        {
            rotationGate.Failed();
            Logger.LogError(e, "Failed to start shard rotation shards");
        }
        finally
        {
            rotationGate.Exit();
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
        var drive = WellKnownAppDrives.ShardRecoveryDrive;
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

    private async Task<SharedSecretEncryptedFileHeader> GetDealerFileForWritingAsync(IOdinContext odinContext)
    {
        return await FileSystem.Query.GetFileByClientUniqueIdForWriting(
            WellKnownAppDrives.ShardRecoveryDrive.Alias, Guid.Parse(DealerShardConfigUid), odinContext);
    }

    /// <summary>
    /// Saves the dealer information for the encrypted shards
    /// </summary>
    internal async Task SaveDealerPackage(DealerShardPackage envelope, IOdinContext odinContext)
    {
        var existingFile = await GetDealerFileForWritingAsync(odinContext);
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
                RemoteTargetDrive = WellKnownAppDrives.ShardRecoveryDrive,
                Priority = OutboxPriority.High
            };

            try
            {
                var clientAuthToken = new ClientAccessToken()
                {
                    Id = configuration.AccountRecovery.AutomatedIdentityKey,
                    AccessTokenHalfKey = configuration.AccountRecovery.AutomatedIdentityKey.ToByteArray().ToSensitiveByteArray(),
                    ClientTokenType = ClientTokenType.PeerAutomatedPasswordRecovery,
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
                            WellKnownAppDrives.ShardRecoveryDrive,
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
                Logger.LogError("Failed while creating outbox item {msg}", ex.Message);
                status.Add(recipient, TransferStatus.EnqueuedFailed);
            }
        }

        return status;
    }

    /// <summary>Sends a friend's shard to their Shard Recovery drive; they need a connection.</summary>
    private async Task<TransferStatus> SendShardAsync(OdinId player, ServerFileHeader file, IOdinContext odinContext)
    {
        var transitOptions = new TransitOptions
        {
            IsTransient = true,
            Recipients = [player.DomainName],
            DisableTransferHistory = true,
            UseAppNotification = true,
            AppNotificationOptions = new AppNotificationOptions
            {
                AppId = SystemAppConstants.SystemAppId,
                TypeId = OwnerAppConstants.PasswordRecoveryRecruitedTypeId,
                TagId = default,
                Silent = false,
                PeerSubscriptionId = default,
                Recipients = null,
                UnEncryptedMessage = $"{odinContext.Tenant.DomainName} has added you as part of their password recovery process. "
            },
            RemoteTargetDrive = WellKnownAppDrives.ShardRecoveryDrive,
            Priority = OutboxPriority.High
        };

        var transferStatusMap = await transferService.SendFile(
            file.FileMetadata.File,
            transitOptions,
            TransferFileType.Normal,
            file.ServerMetadata.FileSystemType,
            odinContext);

        return transferStatusMap[player];
    }

    private async Task WriteNewDealerEnvelopeFile(DealerShardPackage dealerShard, IOdinContext odinContext)
    {
        var driveId = WellKnownAppDrives.ShardRecoveryDrive.Alias;
        var file = await FileSystem.Storage.CreateInternalFileId(driveId, odinContext);

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

        var serverFileHeader = await FileSystem.Storage.CreateServerFileHeader(file, keyHeader, fileMetadata, serverMetadata, odinContext);
        await FileSystem.Storage.WriteNewFileHeader(file, serverFileHeader, odinContext, raiseEvent: true);
    }

    private async Task OverwriteDealerEnvelopeFile(DealerShardPackage dealerShard, Guid existingFileId,
        IOdinContext odinContext)
    {
        var driveId = WellKnownAppDrives.ShardRecoveryDrive.Alias;
        var file = new InternalDriveFileId()
        {
            FileId = existingFileId,
            DriveId = driveId
        };

        var header = await FileSystem.Storage.GetServerFileHeaderForWriting(file, odinContext);
        header.FileMetadata.AppData.Content = DealerShardPackage.Serialize(dealerShard);
        header.FileMetadata.IsEncrypted = false;
        await FileSystem.Storage.UpdateActiveFileHeader(file, header, odinContext, raiseEvent: true);
    }

    /// <summary>
    /// A shard as a file on the transient drive, to be sent to its player.  A gated friend's is a
    /// deposit: its content and key header as the browser made them.  The friend's server can't open
    /// it, so what its checks read is in the file's plain metadata: the shard id (<c>UniqueId</c>) and the
    /// recovery email hash (<c>GroupId</c>); that it is a deposit says the friend must approve its
    /// release (<see cref="PlayerType.Delegate"/>).
    /// </summary>
    private async Task<ServerFileHeader> WriteShardToTempDriveAsync(Guid shardId, string content, KeyHeader keyHeader,
        IOdinContext odinContext, Guid? recoveryEmailHash = null)
    {
        var driveId = SystemDriveConstants.TransientTempDrive.Alias;
        var file = await FileSystem.Storage.CreateInternalFileId(driveId, odinContext);

        var fileMetadata = new FileMetadata(file)
        {
            GlobalTransitId = Guid.NewGuid(),
            AppData = new AppFileMetaData()
            {
                FileType = PlayerEncryptedShardFileType,
                Content = content,
                UniqueId = shardId,
                GroupId = recoveryEmailHash
            },

            IsEncrypted = keyHeader is SealedKeyHeader,
            SenderOdinId = odinContext.Tenant,
            VersionTag = SequentialGuid.CreateGuid(),
            Payloads = []
        };

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = AccessControlList.OwnerOnly,
            AllowDistribution = true
        };

        var serverFileHeader = await FileSystem.Storage.CreateServerFileHeader(file, keyHeader, fileMetadata, serverMetadata, odinContext);
        await FileSystem.Storage.WriteNewFileHeader(file, serverFileHeader, odinContext, raiseEvent: true);

        return serverFileHeader;
    }

    private Task<ServerFileHeader> WritePlayerEncryptedShardToTempDrive(PlayerEncryptedShard shard, IOdinContext odinContext) =>
        WriteShardToTempDriveAsync(shard.Id, PlayerEncryptedShard.Serialize(shard), KeyHeader.Empty(), odinContext);

    /// <summary>
    /// Saves the key used to decrypt the <see cref="PlayerEncryptedShard"/>s
    /// </summary>
    private async Task SaveDistributableKey(SensitiveByteArray distributionKey, IOdinContext odinContext, bool movingOffThePhrase)
    {
        odinContext.Caller.AssertHasMasterKey();

        // The master key, not the recovery phrase: once the browser takes the phrase over the server
        // has no copy of it (docs/zero-knowledge-recovery-plan.md, decision 6).
        var masterKey = odinContext.Caller.GetMasterKey();
        var record = new ShamirKeyRecord()
        {
            ShamirDistributionKeyEncryptedMasterKey = new SymmetricKeyEncryptedAes(secret: distributionKey, dataToEncrypt: masterKey),
            Created = UnixTimeUtc.Now()
        };

        if (configuration.Development.DealsLockThePhrase && !movingOffThePhrase)
        {
            record = await DealLikeBeforeRecoveryInTheBrowserAsync(distributionKey, odinContext) ?? record;
        }

        var n = await Storage.UpsertAsync(tblKeyValue, ShamirRecordStorageId, record);
        if (n != 1)
        {
            throw new OdinSystemException($"Failed to save key. {n} records affected");
        }
    }

    /// <summary>
    /// Tests only (<c>Development:DealsLockThePhrase</c>): a deal as made before recovery in the browser,
    /// locking the phrase.  Null once the phrase is taken over: then there's no phrase to lock.
    /// </summary>
    private async Task<ShamirKeyRecord> DealLikeBeforeRecoveryInTheBrowserAsync(SensitiveByteArray distributionKey,
        IOdinContext odinContext)
    {
        if ((await passwordKeyRecoveryService.GetPhraseStateAsync()).TakenOver)
        {
            return null;
        }

        var phrase = await passwordKeyRecoveryService.GetPhraseForTakeoverAsync(odinContext);

        using var recoveryKey = BIP39Util.DecodeBIP39(phrase);
        return new ShamirKeyRecord
        {
            ShamirDistributionKeyEncryptedRecoveryKey = new SymmetricKeyEncryptedAes(secret: distributionKey, dataToEncrypt: recoveryKey),
            Created = UnixTimeUtc.Now()
        };
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