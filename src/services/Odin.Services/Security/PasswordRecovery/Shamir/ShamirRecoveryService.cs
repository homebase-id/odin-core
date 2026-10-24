using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Login;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Connections;
using Odin.Services.Security.Email;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Odin.Services.Security.PasswordRecovery.Shamir.ShardCollection;
using Odin.Services.Security.PasswordRecovery.Shamir.ShardRequestApproval;
using Odin.Services.Security.ZeroKnowledge;
using Odin.Services.Util;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

/// <summary>
/// Handles scenarios where the Owner has lost their master password
/// and need to get shards from their peer network
/// </summary>
public class ShamirRecoveryService
{
    private readonly ShamirConfigurationService _configurationService;
    private readonly TenantContext _tenantContext;
    private readonly TableKeyValueCached _keyValueTable;
    private readonly IOdinHttpClientFactory _odinHttpClientFactory;
    private readonly CircleNetworkService _circleNetworkService;
    private readonly RecoveryNotifier _recoveryNotifier;
    private readonly PasswordKeyRecoveryService _passwordKeyRecoveryService;
    private readonly IDriveManager _driveManager;
    private readonly TableNonce _nonceTable;
    private readonly OwnerSecretService _secretService;
    private readonly IdentityDatabase _db;
    private readonly IMediator _mediator;
    private readonly ILogger<ShamirRecoveryService> _logger;
    private readonly PlayerShardCollector _playerShardCollector;
    private readonly ShardRequestApprovalCollector _approvalCollector;
    private readonly ZeroKnowledgeKeyService _zeroKnowledgeKeyService;

    /// <summary>
    /// Handles scenarios where the Owner has lost their master password and need to get shards from their peer network
    /// </summary>
    public ShamirRecoveryService(ShamirConfigurationService configurationService,
        TenantContext tenantContext,
        TableKeyValueCached keyValueTable,
        IOdinHttpClientFactory odinHttpClientFactory,
        StandardFileSystem fileSystem,
        IdentityDatabase db,
        IMediator mediator,
        CircleNetworkService circleNetworkService,
        RecoveryNotifier recoveryNotifier,
        PasswordKeyRecoveryService passwordKeyRecoveryService,
        IDriveManager driveManager,
        TableNonce nonceTable,
        OwnerSecretService secretService,
        ZeroKnowledgeKeyService zeroKnowledgeKeyService,
        ILogger<ShamirRecoveryService> logger)
    {
        _zeroKnowledgeKeyService = zeroKnowledgeKeyService;
        _configurationService = configurationService;
        _tenantContext = tenantContext;
        _keyValueTable = keyValueTable;
        _odinHttpClientFactory = odinHttpClientFactory;
        _db = db;
        _mediator = mediator;
        _logger = logger;
        _circleNetworkService = circleNetworkService;
        _recoveryNotifier = recoveryNotifier;
        _passwordKeyRecoveryService = passwordKeyRecoveryService;
        _driveManager = driveManager;
        _nonceTable = nonceTable;
        _secretService = secretService;

        _playerShardCollector = new PlayerShardCollector(fileSystem);
        _approvalCollector = new ShardRequestApprovalCollector(fileSystem, mediator);
    }

    private static readonly Guid ShamirStatusStorageId = Guid.Parse("d2180696-2d18-41e3-8699-c90c0d3aa710");
    private const string ContextKey = "aa575e4a-ffc6-44a1-8ea6-077ee1171a9d";
    private static readonly SingleKeyValueStorage Storage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ContextKey));

    /// <summary>
    /// Sets the identity into recovery mode so peer shard holders can give the parts
    /// </summary>
    /// <remarks>
    /// For a friend deal the browser sends its throwaway public key, and the email's nonce keeps it:
    /// anyone can start recovery, so entering it must come from the browser that started it
    /// (<see cref="EnterRecoveryMode"/>), not from whichever email the owner clicks.
    /// </remarks>
    public async Task InitiateRecoveryModeEntry(string recoveryPublicKeyJwk, IOdinContext odinContext)
    {
        _logger.LogDebug("Initiating recovery mode");
        var current = await GetStatusRecordInternal();
        var pkg = await GetDealerPackageOrFailAsync(odinContext);
        var startKey = pkg.UsesAutomatedRecovery ? null : recoveryPublicKeyJwk;
        var collecting = current?.State == ShamirRecoveryState.AwaitingSufficientDelegateConfirmation;

        // Under way: do nothing, say nothing, to keep any insights from the wrong eyes.  Except "start
        // over" from the browser it is under way for while shards are being collected: a new link,
        // which asks the friends again (EnterRecoveryMode).
        if (IsInRecoveryMode(current) && !(collecting && SameKey(startKey, current.RecoveryPublicKeyJwk)))
        {
            return;
        }

        if (!pkg.UsesAutomatedRecovery)
        {
            ZkBlob.AssertPublicKey(recoveryPublicKeyJwk, nameof(InitiateRecoveryModeRequest.RecoveryPublicKeyJwk));

            // A friend deal without its copies (dealt by the server before recovery in the browser, or
            // forgotten by a reset) could never give anything back; say so now rather than at the end
            if (await _zeroKnowledgeKeyService.GetShamirRecoveryCopiesAsync() == null)
            {
                throw new OdinClientException("Recovery with friends is not set up", OdinClientErrorCode.PasswordRecoveryNotConfigured);
            }
        }

        var players = pkg.Envelopes.Select(p => p.Player).ToList();

        await _recoveryNotifier.EnqueueVerificationEmail(players, RecoveryEmailType.EnterRecoveryMode, nonceData: startKey ?? "");

        if (!collecting)
        {
            // The key the email's link carries, so a browser can tell the recovery it started (the status's
            // RecoveryKeyId); the last start wins.  Recorded once the email is on its way.
            await UpdateStatus(new ShamirRecoveryStatusRecord
            {
                Updated = UnixTimeUtc.Now(),
                State = ShamirRecoveryState.AwaitingOwnerEmailVerificationToEnterRecoveryMode,
                RecoveryPublicKeyJwk = startKey
            });
        }
    }

    public async Task InitiateRecoveryModeExit(IOdinContext odinContext)
    {
        // Only the email: anyone can ask, and nothing changes until the owner confirms the exit with it
        // (ExitRecoveryMode).  Recovery goes on as it was meanwhile.
        await _recoveryNotifier.EnqueueVerificationEmail([], RecoveryEmailType.ExitRecoveryMode);
    }

    public async Task<ShamirRecoveryStatusRedacted> GetStatus(IOdinContext odinContext)
    {
        var status = await GetStatusRecordInternal();
        var maskedEmail = EmailMasker.Mask(_tenantContext.Email);
        if (null == status)
        {
            return new ShamirRecoveryStatusRedacted()
            {
                Email = maskedEmail,
                State = ShamirRecoveryState.None
            };
        }

        return new ShamirRecoveryStatusRedacted
        {
            Updated = status.Updated,
            State = status.State,
            Email = maskedEmail,
            RecoveryKeyId = status.RecoveryPublicKeyJwk == null ? null : ZkSeal.KeyId(status.RecoveryPublicKeyJwk)
        };
    }

    /// <summary>
    /// Puts this identity into recovery mode
    /// </summary>
    /// <remarks>
    /// For a friend deal, the browser posting the email's nonce must hold the throwaway key that
    /// started recovery: otherwise someone who started recovery with a key of their own could wait for
    /// the owner to click that email, and their key would receive the friends' shards.
    /// </remarks>
    public async Task EnterRecoveryMode(Guid nonceId, string recoveryPublicKeyJwk, IOdinContext odinContext)
    {
        // Checked before the link is used up, so the right browser can still open it
        var nonceData = await _recoveryNotifier.PeekNonceDataOrFail(nonceId);
        var startedWith = string.IsNullOrEmpty(nonceData) ? null : nonceData;
        if (startedWith != null && (recoveryPublicKeyJwk == null || !SameKey(recoveryPublicKeyJwk, startedWith)))
        {
            throw new OdinClientException("Open this link in the browser where you started recovery", OdinClientErrorCode.RecoveryNotReady);
        }

        // A friend deal needs the key the friends seal to; a link made without one (an automated deal's,
        // or another email's) can't enter it
        var package = await GetDealerPackageOrFailAsync(odinContext);
        if (!package.UsesAutomatedRecovery && startedWith == null)
        {
            throw new OdinClientException("Start recovery again in this browser", OdinClientErrorCode.RecoveryNotReady);
        }

        // A second link ("send it again") to a recovery already under way: only from the browser it is
        // under way for.  While shards are being collected it asks the friends again (one may have been
        // unreachable); once they're in, it changes nothing.
        var current = await GetStatusRecordInternal();
        var underWay = IsInRecoveryMode(current);
        if (underWay && !SameKey(current.RecoveryPublicKeyJwk, startedWith))
        {
            throw new OdinClientException("Recovery is under way in another browser", OdinClientErrorCode.RecoveryUnderWayElsewhere);
        }

        await _recoveryNotifier.GetNonceDataOrFail(nonceId);

        if (underWay && current.State != ShamirRecoveryState.AwaitingSufficientDelegateConfirmation)
        {
            return;
        }

        if (!underWay)
        {
            await UpdateStatus(new ShamirRecoveryStatusRecord()
            {
                Updated = UnixTimeUtc.Now(),
                State = ShamirRecoveryState.AwaitingSufficientDelegateConfirmation,
                RecoveryPublicKeyJwk = startedWith
            });
        }

        var hashedRecoveryEmail = await _passwordKeyRecoveryService.GetHashedRecoveryEmail();
        //TODO: move this to an outbox call

        // notify all players this identity needs their password shards
        foreach (var envelope in package.Envelopes)
        {
            var client = await CreateClientAsync(envelope.Player.OdinId);
            var response = await client.RequestShard(new RetrieveShardRequest
            {
                ShardId = envelope.ShardId,
                HashedRecoveryEmail = hashedRecoveryEmail,
                RecoveryPublicKeyJwk = startedWith
            });

            if (response.IsSuccessStatusCode)
            {
                var retrieveShardResult = response.Content;
                if (retrieveShardResult.ResultType == RetrieveShardResultType.Complete)
                {
                    // From the player itself: this server called it
                    await CollectShardAsync(retrieveShardResult, envelope.Player.OdinId, odinContext, package);
                }

                if (retrieveShardResult.ResultType == RetrieveShardResultType.WaitingForPlayer)
                {
                    //hmm, do we tell the user anything or just go into waiting mode?
                }
            }
        }
    }

    public async Task ExitRecoveryMode(Guid nonceId, string token, IOdinContext odinContext)
    {
        await _recoveryNotifier.GetNonceDataOrFail(nonceId);
        await ExitRecoveryModeInternal(odinContext);
    }

    /// <summary>Ends recovery mode once a reset with a reset key set a new password.</summary>
    internal async Task EndRecoveryModeAfterResetAsync(IOdinContext odinContext)
    {
        await ExitRecoveryModeInternal(odinContext);
    }

    public async Task ForceExitRecoveryMode(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        await ExitRecoveryModeInternal(odinContext);
    }
    
    public async Task<RetrieveShardResult> HandleReleaseShardRequest(RetrieveShardRequest request, IOdinContext odinContext)
    {
        var requester = odinContext.Caller.OdinId.GetValueOrDefault();

        // look up the shard info
        var stored = await _configurationService.GetShardStoredForDealer(request.ShardId, odinContext);
        var (shard, sender, _) = stored;

        if (sender != requester)
        {
            throw new OdinSecurityException("invalid requester");
        }

        if (shard == null)
        {
            throw new OdinClientException("Invalid shard id");
        }

        if (shard.RecoveryEmailHash != request.HashedRecoveryEmail)
        {
            throw new OdinSecurityException("Invalid RecoveryEmailHash");
        }

        if (shard.Player.Type == PlayerType.Automatic)
        {
            // return it now
            return new RetrieveShardResult
            {
                ResultType = RetrieveShardResultType.Complete,
                Shard = shard
            };
        }

        if (shard.Player.Type == PlayerType.Delegate)
        {
            // A gated shard is sealed to this identity's browser, which seals it again to the recovering
            // browser's key when the owner approves
            await _configurationService.AssertCanReleaseAsync(stored);
            ZkBlob.AssertPublicKey(request.RecoveryPublicKeyJwk, nameof(request.RecoveryPublicKeyJwk));

            var r = new ShardApprovalRequest
            {
                ShardId = shard.Id,
                Dealer = sender.GetValueOrDefault(),
                Created = UnixTimeUtc.Now(),
                RecoveryPublicKeyJwk = request.RecoveryPublicKeyJwk
            };

            await _approvalCollector.SaveRequest(r, odinContext);

            return new RetrieveShardResult
            {
                ResultType = RetrieveShardResultType.WaitingForPlayer,
                Shard = shard,
            };
        }

        throw new OdinSystemException($"How did we get here?  The player type is not delegate or " +
                                      $"automatic. It is {shard.Player.Type} for Player {shard.Player.OdinId} and " +
                                      $"id {shard.Id}");
    }

    /// <summary>
    /// A player has sent a shard to the dealer (<c>accept-player-shard</c>).  Only the shard's own player
    /// may: anyone connected could otherwise fill the collector with junk.
    /// </summary>
    public async Task HandleAcceptRecoveryShard(RetrieveShardResult result, IOdinContext odinContext)
    {
        var caller = odinContext.Caller.OdinId ?? throw new OdinSecurityException("Unknown caller");
        await CollectShardAsync(result, caller, odinContext);
    }

    /// <summary>
    /// Collects a shard from <paramref name="player"/>, which must be the player the dealer dealt it to.
    /// <paramref name="package"/> is the dealer package when the caller has it already.
    /// </summary>
    private async Task CollectShardAsync(RetrieveShardResult result, OdinId player, IOdinContext odinContext,
        DealerShardPackage package = null)
    {
        var status = await GetStatusRecordInternal();
        if (!IsInRecoveryMode(status))
        {
            throw new OdinClientException("Not in recovery mode");
        }

        if (result.ResultType != RetrieveShardResultType.Complete)
        {
            throw new OdinClientException("Invalid result type");
        }

        odinContext = OdinContextUpgrades.UpgradeToByPassAclCheck(
            WellKnownAppDrives.ShardRecoveryDrive,
            DrivePermission.Read,
            odinContext);

        await using var tx = await _db.BeginStackedTransactionAsync();

        package ??= await _configurationService.GetDealerShardPackage(odinContext);
        var envelope = result.Shard?.Player?.OdinId != player
            ? null
            : package?.Envelopes.SingleOrDefault(e => e.ShardId == result.Shard.Id && e.Player.OdinId == player);
        if (envelope == null)
        {
            throw new OdinSecurityException($"{player} did not hold shard {result.Shard?.Id}");
        }

        // A gated friend's browser sealed it to the recovering browser; anything else is junk that
        // would only spoil the rebuild
        if (envelope.Player.Type == PlayerType.Delegate && !package.UsesAutomatedRecovery &&
            (status.RecoveryPublicKeyJwk == null || !ZkSeal.IsSealedTo(result.Shard.DealerEncryptedShard, status.RecoveryPublicKeyJwk)))
        {
            throw new OdinClientException("The shard is not sealed to the recovering browser", OdinClientErrorCode.ArgumentError);
        }

        await _playerShardCollector.SavePlayerShard(result.Shard, odinContext);
        if (status.State == ShamirRecoveryState.AwaitingRecoveringBrowser)
        {
            // Told once, at the threshold; later approvals only add shards the browser may need
            tx.Commit();
            return;
        }

        var collectedShards = await _playerShardCollector.GetCollectShards(odinContext);

        if (collectedShards.Count >= package.MinMatchingShards && !package.UsesAutomatedRecovery)
        {

            // The dealer can't open the gated shards: the browser that started recovery rebuilds the
            // secret (GetRecoveryBundleAsync).  The email carries nothing.
            await UpdateStatus(status with
            {
                Updated = UnixTimeUtc.Now(),
                State = ShamirRecoveryState.AwaitingRecoveringBrowser
            });
            await _recoveryNotifier.EnqueueFriendsApprovedEmail();

            await _mediator.Publish(new ShamirPasswordRecoverySufficientShardsCollectedNotification()
            {
                OdinContext = odinContext
            });
        }
        else if (collectedShards.Count >= package.MinMatchingShards)
        {
            var (finalRecoveryKey, finalInfo) = await PrepareFinalKeys(collectedShards, package);
            await _recoveryNotifier.EnqueueFinalizeRecoveryEmail(finalInfo, finalRecoveryKey);

            await UpdateStatus(new ShamirRecoveryStatusRecord
            {
                Updated = UnixTimeUtc.Now(),
                State = ShamirRecoveryState.AwaitingOwnerFinalization,
            });

            await _mediator.Publish(new ShamirPasswordRecoverySufficientShardsCollectedNotification()
            {
                OdinContext = odinContext
            });
        }
        else
        {
            // collect the shards, so I can piece together my password
            int remainingRequired = package.MinMatchingShards - collectedShards.Count;
            await _mediator.Publish(new ShamirPasswordRecoveryShardCollectedNotification
            {
                OdinContext = odinContext,
                Sender = result.Shard.Player.OdinId,
                AdditionalMessage = $"You need {remainingRequired} more shards to recover your identity."
            });
        }

        tx.Commit();
    }

    public async Task FinalizeRecovery(Guid nonceId, Guid finalKey, PasswordReply passwordReply,
        IOdinContext odinContext)
    {
        // One transaction, and recovery mode ends last: a failure leaves the link and the collected
        // shards as they were, so the owner can try again.
        await using var tx = await _db.BeginStackedTransactionAsync();

        var data = await _recoveryNotifier.GetNonceDataOrFail(nonceId);
        var recoveryInfo = OdinSystemSerializer.DeserializeOrThrow<FinalRecoveryInfo>(data);
        var finalKeyBytes = finalKey.ToByteArray().ToSensitiveByteArray();
        using var distributionKey = AesCbc.Decrypt(recoveryInfo.Cipher, finalKeyBytes, recoveryInfo.Iv).ToSensitiveByteArray();
        using var masterKey = await _configurationService.DecryptMasterKeyAsync(distributionKey);

        await _secretService.ResetPasswordUsingMasterKeyAsync(masterKey, passwordReply);
        await ExitRecoveryModeInternal(odinContext);
        tx.Commit();
    }

    public async Task<List<ShardApprovalRequest>> GetShardRequestList(IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsOwner();
        var requests = await _approvalCollector.GetRequests(odinContext);
        return requests.ToList();
    }

    /// <summary>
    /// Releases a gated shard to its dealer.  The owner's browser opened it (it is sealed to this
    /// identity's Shard Recovery write-only key) and sealed the piece again to the recovering browser's
    /// throwaway key: <paramref name="sealedShard"/>.  This server only passes it on.
    /// </summary>
    public async Task ApproveShardRequest(Guid shardId, OdinId odinId, int index, byte[] sealedShard, IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsOwner();

        var request = await _approvalCollector.GetRequest(shardId, odinContext);
        if (null == request)
        {
            throw new OdinClientException("Invalid shard id");
        }

        if (request.Dealer != odinId)
        {
            throw new OdinClientException("Invalid dealer on shard id");
        }

        var stored = await _configurationService.GetShardStoredForDealer(shardId, odinContext);
        var (shard, sender, _) = stored;

        if (sender != odinId)
        {
            throw new OdinClientException("Invalid dealer on shard id");
        }

        if (null == shard)
        {
            throw new OdinClientException("Invalid shard id");
        }

        await _configurationService.AssertCanReleaseAsync(stored);
        OdinValidationUtils.AssertIsTrue(index >= 1, "A shard's index starts at 1");
        if (sealedShard == null || !ZkSeal.IsSealedTo(sealedShard, request.RecoveryPublicKeyJwk))
        {
            throw new OdinClientException("The shard must be sealed to the recovering browser's key", OdinClientErrorCode.ArgumentError);
        }

        var client = await CreateClientAsyncWithToken(sender.GetValueOrDefault(), null, odinContext);
        var response = await client.SendPlayerShard(new RetrieveShardResult
        {
            ResultType = RetrieveShardResultType.Complete,
            Shard = new PlayerEncryptedShard
            {
                Id = shard.Id,
                Index = index,
                RecoveryEmailHash = shard.RecoveryEmailHash,
                Player = shard.Player,
                Created = shard.Created,
                DealerEncryptedShard = sealedShard
            }
        });

        // the dealer refuses a shard from a delegate it is no longer connected to (#1885); say so,
        // and keep the request so the approval can be retried
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Dealer {dealer} did not accept shard {shardId}: {status}", odinId, shardId, response.StatusCode);
            throw new OdinClientException(
                $"{odinId} did not accept the shard ({(int)response.StatusCode}). You may no longer be connected to them.");
        }

        await _approvalCollector.DeleteRequest(shardId, odinContext);
        // tx.Commit();
    }

    public async Task RejectShardRequest(Guid shardId, OdinId odinId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsOwner();

        var request = await _approvalCollector.GetRequest(shardId, odinContext);
        if (null == request)
        {
            throw new OdinClientException("Invalid shard id");
        }

        if (request.Dealer != odinId)
        {
            throw new OdinClientException("Invalid dealer on shard id");
        }

        await _approvalCollector.DeleteRequest(shardId, odinContext);
    }

    /// <summary>
    /// What the browser that started recovery with friends rebuilds the secret from, sealed to its
    /// throwaway key, once enough friends approved.  No login: only that browser opens it.
    /// </summary>
    public async Task<ShamirRecoveryBundleResponse> GetRecoveryBundleAsync(IOdinContext odinContext)
    {
        var status = await GetStatusRecordInternal();
        if (status?.State != ShamirRecoveryState.AwaitingRecoveringBrowser || status.RecoveryPublicKeyJwk == null)
        {
            throw new OdinClientException("Not enough friends have approved yet", OdinClientErrorCode.RecoveryNotReady);
        }

        odinContext = OdinContextUpgrades.UpgradeToByPassAclCheck(WellKnownAppDrives.ShardRecoveryDrive, DrivePermission.Read, odinContext);
        var package = await _configurationService.GetDealerShardPackage(odinContext);
        var copies = await _zeroKnowledgeKeyService.GetShamirRecoveryCopiesAsync();
        if (package == null || copies == null)
        {
            throw new OdinClientException("Recovery with friends is not set up", OdinClientErrorCode.PasswordRecoveryNotConfigured);
        }

        var bundle = new ShamirRecoveryBundle
        {
            MinMatchingShards = package.MinMatchingShards,
            Envelopes = package.Envelopes,
            Shards = await _playerShardCollector.GetCollectShards(odinContext),
            Copies = copies
        };

        var json = OdinSystemSerializer.Serialize(bundle).ToUtf8ByteArray();
        return new ShamirRecoveryBundleResponse { SealedBundle64 = ZkSeal.Seal(json, status.RecoveryPublicKeyJwk).ToBase64() };
    }

    private async Task<(Guid finalRecoveryKey, FinalRecoveryInfo finalInfo)> PrepareFinalKeys(
        List<PlayerEncryptedShard> collectedShards,
        DealerShardPackage package)
    {
        var decryptedShards = new List<ShamirSecretSharing.ShamirShard>();
        foreach (var shard in collectedShards)
        {
            var envelope = package.Envelopes.FirstOrDefault(e => e.Player.OdinId == shard.Player.OdinId
                                                                 && e.ShardId == shard.Id);
            if (null == envelope?.EncryptionKey)
            {
                _logger.LogWarning("Missing key for player: [{player}] for shardId: [{sid}]; continuing.",
                    shard.Player.OdinId,
                    shard.Id);

                continue;
            }

            var decryptedShard = AesCbc.Decrypt(shard.DealerEncryptedShard, envelope.EncryptionKey, envelope.EncryptionIv);
            var shamirShard = new ShamirSecretSharing.ShamirShard(shard.Index, decryptedShard);
            decryptedShards.Add(shamirShard);
        }

        // decrypt all the shards
        var distributionKey = ShamirSecretSharing.ReconstructShamirSecret(decryptedShards.OrderBy(s => s.Index).ToList(),
            ShamirConfigurationService.DistributionKeyLength);

        // Put the distribution key in the nonce, locked by the key the email carries; finalize opens the
        // master key with it.  Opened here once, so a wrong rebuild fails now and not at finalize.
        (await _configurationService.DecryptMasterKeyAsync(distributionKey.ToSensitiveByteArray())).Wipe();

        var finalRecoveryKey = ByteArrayUtil.GetRandomCryptoGuid();

        var (iv, cipher) = AesCbc.Encrypt(distributionKey, finalRecoveryKey.ToByteArray().ToSensitiveByteArray());
        var finalInfo = new FinalRecoveryInfo()
        {
            Iv = iv,
            Cipher = cipher
        };

        return (finalRecoveryKey, finalInfo);
    }

    /// <summary>Whether two public keys are the same key; a malformed one matches nothing.</summary>
    private static bool SameKey(string jwk, string otherJwk)
    {
        if (jwk == null || otherJwk == null)
        {
            return jwk == otherJwk;
        }

        try
        {
            return ZkSeal.RawPublicKey(jwk).SequenceEqual(ZkSeal.RawPublicKey(otherJwk));
        }
        catch (OdinClientException)
        {
            return false;
        }
    }

    /// <summary>The dealer package, read as recovery reads it with no login; refused when there is none.</summary>
    private async Task<DealerShardPackage> GetDealerPackageOrFailAsync(IOdinContext odinContext)
    {
        var context = OdinContextUpgrades.UpgradeToByPassAclCheck(WellKnownAppDrives.ShardRecoveryDrive, DrivePermission.Read, odinContext);
        return await _configurationService.GetDealerShardPackage(context)
               ?? throw new OdinClientException("Password recovery not configured", OdinClientErrorCode.PasswordRecoveryNotConfigured);
    }

    private static bool IsInRecoveryMode(ShamirRecoveryStatusRecord status)
    {
        if (status == null)
        {
            return false;
        }

        return status.State == ShamirRecoveryState.AwaitingSufficientDelegateConfirmation ||
               status.State == ShamirRecoveryState.AwaitingOwnerEmailVerificationToExitRecoveryMode ||
               status.State == ShamirRecoveryState.AwaitingOwnerFinalization ||
               status.State == ShamirRecoveryState.AwaitingRecoveringBrowser;
    }

    private async Task<ShamirRecoveryStatusRecord> GetStatusRecordInternal()
    {
        var record = await Storage.GetAsync<ShamirRecoveryStatusRecord>(_keyValueTable, ShamirStatusStorageId);
        return record;
    }

    private async Task UpdateStatus(ShamirRecoveryStatusRecord statusRecord)
    {
        await Storage.UpsertAsync(_keyValueTable, ShamirStatusStorageId, statusRecord);
    }

    private async Task<IPeerPasswordRecoveryHttpClient> CreateClientAsync(OdinId odinId)
    {
        var httpClient = await _odinHttpClientFactory.CreateClientAsync<IPeerPasswordRecoveryHttpClient>(odinId);
        return httpClient;
    }

    private async Task<IPeerPasswordRecoveryHttpClient> CreateClientAsyncWithToken(OdinId odinId,
        FileSystemType? fileSystemType,
        IOdinContext odinContext)
    {
        //Note here we override the permission check because we have either UseTransitWrite or UseTransitRead
        var icr = await _circleNetworkService.GetIcrAsync(odinId, odinContext, overrideHack: true, tryUpgradeEncryption: true);
        var authToken = icr.IsConnected() ? icr.CreateClientAuthToken(odinContext.PermissionsContext.GetIcrKey()) : null;
        var httpClient =
            await _odinHttpClientFactory.CreateClientUsingAccessTokenAsync<IPeerPasswordRecoveryHttpClient>(odinId, authToken, fileSystemType);
        return httpClient;
    }

    private async Task ExitRecoveryModeInternal(IOdinContext odinContext)
    {
        await Storage.DeleteAsync(_keyValueTable, ShamirStatusStorageId);

        var drive = await _driveManager.GetDriveAsync(WellKnownAppDrives.ShardRecoveryDrive.Alias, false);
        if (null != drive)
        {
            await _playerShardCollector.DeleteCollectedShards(odinContext);
        }
    }

}