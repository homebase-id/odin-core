using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Threading;
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
using Odin.Core.Storage.Concurrency;
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
using Refit;

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
    private readonly INodeLock _nodeLock;
    private readonly OwnerSecretService _secretService;
    private readonly IdentityDatabase _db;
    private readonly IMediator _mediator;
    private readonly ILogger<ShamirRecoveryService> _logger;
    private readonly PlayerShardCollector _playerShardCollector;
    private readonly ShardRequestApprovalCollector _approvalCollector;
    private readonly AutomaticShardReleases _automaticReleases;
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
        INodeLock nodeLock,
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
        _nodeLock = nodeLock;
        _secretService = secretService;

        _playerShardCollector = new PlayerShardCollector(fileSystem);
        _approvalCollector = new ShardRequestApprovalCollector(fileSystem, mediator);
        _automaticReleases = new AutomaticShardReleases(keyValueTable);
    }

    /// <summary>
    /// How long this server waits for a peer in recovery, from setting up the call: a holder asked at
    /// entry or for a click on its link, and the dealer a friend's approval goes to.
    /// </summary>
    private static readonly TimeSpan HolderAskTimeout = TimeSpan.FromSeconds(20);

    /// <summary>How long a holder waits for its per-shard lock: below the dealer's wait.</summary>
    private static readonly TimeSpan HolderLockWait = HolderAskTimeout - TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long collecting a shard waits for the collect lock.  A click waits for the holder and then
    /// this: well under a proxy's cut.
    /// </summary>
    private static readonly TimeSpan CollectLockWait = TimeSpan.FromSeconds(15);

    private NodeLockKey CollectLock => NodeLockKey.Create(nameof(ShamirRecoveryService), "collect", _tenantContext.HostOdinId.DomainName);

    /// <summary>
    /// Takes <paramref name="key"/>, waiting at most <paramref name="wait"/>; a wait that runs out is
    /// logged as such (a canceled wait would read like a client that went away) and is a retryable
    /// error.
    /// </summary>
    private async Task<IAsyncDisposable> LockOrSayBusyAsync(NodeLockKey key, TimeSpan wait, string what)
    {
        using var waiting = new CancellationTokenSource(wait);
        try
        {
            return await _nodeLock.LockAsync(key, timeout: wait, cancellationToken: waiting.Token);
        }
        catch (Exception e) when (waiting.IsCancellationRequested || e is RedisLockException)
        {
            _logger.LogWarning("Waited {wait} for the lock on {what}; it is still taken", wait, what);
            throw new OdinClientException("Busy with another request; try again", OdinClientErrorCode.RecoveryBusy);
        }
    }

    // Internal for the tests that store a status as an older release did
    internal static readonly Guid ShamirStatusStorageId = Guid.Parse("d2180696-2d18-41e3-8699-c90c0d3aa710");
    private const string ContextKey = "aa575e4a-ffc6-44a1-8ea6-077ee1171a9d";
    internal static readonly SingleKeyValueStorage Storage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ContextKey));

    /// <summary>
    /// Sets the identity into recovery mode so peer shard holders can give the parts
    /// </summary>
    /// <remarks>
    /// The browser sends its throwaway public key, and the email's nonce keeps it: anyone can start
    /// recovery, so entering it must come from the browser that started it
    /// (<see cref="EnterRecoveryMode"/>), not from whichever email the owner clicks.  Every holder's
    /// shard is sealed to that key.
    /// </remarks>
    public async Task InitiateRecoveryModeEntry(string recoveryPublicKeyJwk, IOdinContext odinContext)
    {
        _logger.LogDebug("Initiating recovery mode");
        var current = await GetStatusRecordInternal();
        var pkg = await GetDealerPackageOrFailAsync(odinContext);
        var awaitingShards = current?.State == ShamirRecoveryState.AwaitingSufficientDelegateConfirmation;

        // Under way: do nothing, say nothing, to keep any insights from the wrong eyes.  Except "start
        // over" while shards are being collected, from the browser it is under way for: a new link,
        // which asks the holders again (EnterRecoveryMode).
        if (IsCollecting(current) && !(awaitingShards && SameKey(recoveryPublicKeyJwk, current.RecoveryPublicKeyJwk)))
        {
            return;
        }

        ZkBlob.AssertPublicKey(recoveryPublicKeyJwk, nameof(InitiateRecoveryModeRequest.RecoveryPublicKeyJwk));

        // Said now rather than at the end: a deal that can give nothing back
        await GetRecoveryCopiesOrFailAsync(pkg);

        var players = pkg.Envelopes.Select(p => p.Player).ToList();

        await _recoveryNotifier.EnqueueVerificationEmail(await _passwordKeyRecoveryService.GetRecoveryEmailAsync(), players,
            RecoveryEmailType.EnterRecoveryMode, nonceData: recoveryPublicKeyJwk);

        if (!awaitingShards)
        {
            // The key the email's link carries, so a browser can tell the recovery it started (the status's
            // RecoveryKeyId); the last start wins.  Recorded once the email is on its way.
            await UpdateStatus(new ShamirRecoveryStatusRecord
            {
                Updated = UnixTimeUtc.Now(),
                State = ShamirRecoveryState.AwaitingOwnerEmailVerificationToEnterRecoveryMode,
                RecoveryPublicKeyJwk = recoveryPublicKeyJwk
            });
        }
    }

    public async Task InitiateRecoveryModeExit(IOdinContext odinContext)
    {
        // Only the email: anyone can ask, and nothing changes until the owner confirms the exit with it
        // (ExitRecoveryMode).  Recovery goes on as it was meanwhile.
        await _recoveryNotifier.EnqueueVerificationEmail(await _passwordKeyRecoveryService.GetRecoveryEmailAsync(), [],
            RecoveryEmailType.ExitRecoveryMode);
    }

    public async Task<ShamirRecoveryStatusRedacted> GetStatus(IOdinContext odinContext)
    {
        var status = await GetStatusRecordInternal();
        var maskedEmail = EmailMasker.Mask(await _passwordKeyRecoveryService.GetRecoveryEmailAsync());
        if (null == status)
        {
            return new ShamirRecoveryStatusRedacted()
            {
                Email = maskedEmail,
                State = ShamirRecoveryState.None
            };
        }

        var redacted = new ShamirRecoveryStatusRedacted
        {
            Updated = status.Updated,
            State = status.State,
            Email = maskedEmail,
            RecoveryKeyId = ZkSeal.KeyId(status.RecoveryPublicKeyJwk)
        };

        // The slots, only while shards are awaited (the page shows them then): the holders and which
        // are in, by name only to the browser that started
        if (status.State != ShamirRecoveryState.AwaitingSufficientDelegateConfirmation)
        {
            return redacted;
        }

        var package = await GetDealerPackageAsync(odinContext);
        if (package == null)
        {
            return redacted;
        }

        var collected = await CollectedShardIdsAsync(odinContext);
        var slots = package.Envelopes
            .Select(e => new RecoverySlot { Player = e.Player, Filled = collected.Contains(e.ShardId) })
            .ToList();

        return redacted with
        {
            Needed = package.MinMatchingShards,
            Filled = ShardsIn(package, collected),
            SealedSlots64 = ZkSeal.Seal(OdinSystemSerializer.Serialize(slots).ToUtf8ByteArray(), status.RecoveryPublicKeyJwk).ToBase64()
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

        // Every holder seals to the key the start carried; a link made without one (another email's)
        // can't enter
        var package = await GetDealerPackageOrFailAsync(odinContext);
        if (startedWith == null)
        {
            throw new OdinClientException("Start recovery again in this browser", OdinClientErrorCode.RecoveryNotReady);
        }

        // A second link ("send it again") to a recovery already under way: only from the browser it is
        // under way for.  While shards are being collected it asks the holders again (one may have been
        // unreachable); once they're in, it changes nothing.
        var current = await GetStatusRecordInternal();
        var underWay = IsCollecting(current);
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
            // A fresh recovery starts with none: shards a recovery from before this release left
            // behind, unsealed, would count without being of use
            await _playerShardCollector.DeleteCollectedShards(odinContext);
            await UpdateStatus(new ShamirRecoveryStatusRecord()
            {
                Updated = UnixTimeUtc.Now(),
                State = ShamirRecoveryState.AwaitingSufficientDelegateConfirmation,
                RecoveryPublicKeyJwk = startedWith
            });
        }

        // The hash the deal was dealt with: after an email change the holders refuse until the browser
        // deals again (ZkStatusResponse.DealNeedsDealing).  A deal from before deals recorded it is
        // server-made, and hashed the address as stored.
        var recoveryEmail = await _passwordKeyRecoveryService.GetRecoveryEmailAsync();
        var hashedRecoveryEmail = package.RecoveryEmailHash ?? PasswordKeyRecoveryService.LegacyHashRecoveryEmail(recoveryEmail);
        //TODO: move this to an outbox call

        // Ask every holder whose shard isn't in yet ("start over" asks again), all at once: one that
        // hangs must not hold up the rest.  None answers with its shard: a gated friend approves in
        // their browser, and an automatic holder emails the owner a link (FillSlotAsync).  The calls
        // touch no database, so they need no scope of their own.
        var collected = underWay ? await CollectedShardIdsAsync(odinContext) : [];
        await Task.WhenAll(package.Envelopes.Where(e => !collected.Contains(e.ShardId)).Select(async envelope =>
        {
            // One holder that can't be reached must not keep the others from being asked
            try
            {
                // Bounded, so the browser's request never waits on a holder that hangs
                using var timeout = new CancellationTokenSource(HolderAskTimeout);
                var client = await CreateClientAsync(envelope.Player.OdinId);
                var response = await client.RequestShard(new RetrieveShardRequest
                {
                    ShardId = envelope.ShardId,
                    HashedRecoveryEmail = hashedRecoveryEmail,
                    RecoveryPublicKeyJwk = startedWith,
                    RecoveryEmail = envelope.Player.Type == PlayerType.Automatic ? recoveryEmail : null
                }, timeout.Token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("{holder} did not take the request for shard {shardId}: {status}",
                        envelope.Player.OdinId, envelope.ShardId, response.StatusCode);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "{holder} could not be asked for shard {shardId}", envelope.Player.OdinId, envelope.ShardId);
            }
        }));
    }

    /// <summary>
    /// The owner clicked an automatic holder's email link, on any device: passes its code to the holder,
    /// which releases its shard sealed to the recovering browser's key.  No login: the code is the
    /// proof, and only the browser that started opens what it releases.
    /// </summary>
    public async Task FillSlotAsync(Guid shardId, string code, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNullOrEmpty(code, nameof(code));

        // Checked first: a recovery that is over takes no shard
        if (!IsCollecting(await GetStatusRecordInternal()))
        {
            throw NoRecoveryUnderWay(OdinClientErrorCode.RecoveryNotReady);
        }

        var package = await GetDealerPackageOrFailAsync(odinContext);
        var envelope = package.Envelopes.SingleOrDefault(e => e.ShardId == shardId && e.Player.Type == PlayerType.Automatic)
                       ?? throw new OdinClientException("This link no longer works", OdinClientErrorCode.ShardLinkNoLongerValid);

        ApiResponse<RetrieveShardResult> response = null;
        try
        {
            // Bounded, so the browser hears "try again" rather than a cut connection
            using var timeout = new CancellationTokenSource(HolderAskTimeout);
            var client = await CreateClientAsync(envelope.Player.OdinId);
            response = await client.ReleaseAutomaticShard(new ReleaseAutomaticShardRequest { ShardId = shardId, Code = code }, timeout.Token);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "{holder} could not be reached for shard {shardId}", envelope.Player.OdinId, shardId);
        }

        if (response?.IsSuccessStatusCode != true || response.Content?.ResultType != RetrieveShardResultType.Complete)
        {
            _logger.LogInformation("{holder} did not release shard {shardId}: {status}", envelope.Player.OdinId, shardId, response?.StatusCode);

            // The holder itself refused (its own error code: a wrong, replaced or expired link, or a shard
            // it doesn't hold for this dealer): that is for good.  Anything else (no answer, a timeout, a
            // failure on the way) may pass: the same link still works.
            throw HolderRefused(response)
                ? new OdinClientException("This link no longer works", OdinClientErrorCode.ShardLinkNoLongerValid)
                : new OdinClientException("The holder could not be reached; try the same link again later",
                    OdinClientErrorCode.ShardHolderUnreachable);
        }

        // From the holder itself: this server called it.  A recovery that ended or started over since the
        // check above is no fault of the link, which works again for a new start inside the holder's
        // email interval; a shard sealed to an earlier start's key is (the holder wasn't reached since).
        await CollectShardAsync(response.Content, envelope.Player.OdinId, odinContext, package, OdinClientErrorCode.RecoveryNotReady);
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

        // Every holder's shard goes to the recovering browser's key
        ZkBlob.AssertPublicKey(request.RecoveryPublicKeyJwk, nameof(request.RecoveryPublicKeyJwk));

        if (shard.Player.Type == PlayerType.Automatic)
        {
            // Released only through the owner's inbox: this holder emails the recovery address a link.
            // The address must match the hash stored at the deal, so a dealer that isn't the owner (its
            // server taken over, or a copy of its data) can't send the link elsewhere.
            if (!MailAddress.TryCreate(request.RecoveryEmail, out var email) ||
                !PasswordKeyRecoveryService.MatchesRecoveryEmailHash(request.RecoveryEmail, shard.RecoveryEmailHash))
            {
                throw new OdinSecurityException("Invalid recovery email");
            }

            // Early, before the request is recorded: a link that can't be emailed must not hold off the
            // next one (the notifier checks again, for its other callers' sake)
            _recoveryNotifier.AssertEmailEnabled();

            // One request per shard at a time: a second one must see whether the first one's email went.
            // Its wait is bounded below the dealer's, so a queued request rarely acts after the dealer
            // gave up (and then the email still carries a working link).
            await using var oneAtATime = await LockOrSayBusyAsync(
                NodeLockKey.Create(nameof(ShamirRecoveryService), "release", _tenantContext.HostOdinId.DomainName, shard.Id.ToString()),
                HolderLockWait, $"shard {shard.Id}");
            var code = await _automaticReleases.RequestAsync(shard.Id, requester, request.RecoveryPublicKeyJwk);
            if (code != null)
            {
                try
                {
                    await _recoveryNotifier.EnqueueReleaseShardEmail(email, requester, shard.Id, code);
                }
                catch (Exception e)
                {
                    // It may not have gone out: the next request emails again.  The code stays, in case
                    // it did after all.
                    _logger.LogWarning(e, "The link for shard {shardId} may not have been emailed", shard.Id);
                    await _automaticReleases.AllowResendAsync(shard.Id, code);
                    throw;
                }
            }

            return new RetrieveShardResult { ResultType = RetrieveShardResultType.WaitingForPlayer };
        }

        if (shard.Player.Type == PlayerType.Delegate)
        {
            // A gated shard is sealed to this identity's browser, which seals it again to the recovering
            // browser's key when the owner approves
            await _configurationService.AssertCanReleaseAsync(stored);

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
    /// As an automatic holder: the owner clicked the link this holder emailed, and the dealer passes its
    /// code on.  Releases the shard sealed to the key the dealer's latest request carried.
    /// </summary>
    public async Task<RetrieveShardResult> HandleReleaseAutomaticShardAsync(ReleaseAutomaticShardRequest request, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        var requester = odinContext.Caller.OdinId.GetValueOrDefault();
        var (shard, sender, _) = await _configurationService.GetShardStoredForDealer(request.ShardId, odinContext);
        if (shard == null || sender != requester || shard.Player.Type != PlayerType.Automatic)
        {
            // The link's own code: the dealer tells this refusal from a failure on the way
            throw new OdinClientException("This link no longer works", OdinClientErrorCode.ShardLinkNoLongerValid);
        }

        var release = await _automaticReleases.FindAsync(shard.Id, requester, request.Code)
                      ?? throw new OdinClientException("This link no longer works", OdinClientErrorCode.ShardLinkNoLongerValid);

        // The request stays until a newer email replaces it or it expires: a failure on the dealer's
        // side must not use the link up, and the dealer keeps one copy of a shard however often it
        // comes
        return new RetrieveShardResult
        {
            ResultType = RetrieveShardResultType.Complete,
            Shard = shard with { DealerEncryptedShard = ZkSeal.Seal(shard.DealerEncryptedShard, release.RecoveryPublicKeyJwk) }
        };
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
    /// <paramref name="package"/> is the dealer package when the caller has it already;
    /// <paramref name="noRecoveryCode"/> the code when no recovery is collecting (by default, the one
    /// that tells a holder its link or request won't do any more).
    /// </summary>
    private async Task CollectShardAsync(RetrieveShardResult result, OdinId player, IOdinContext odinContext,
        DealerShardPackage package = null, OdinClientErrorCode noRecoveryCode = OdinClientErrorCode.ShardLinkNoLongerValid)
    {
        // The checks first, outside the lock: anyone in the network can call accept-player-shard, and
        // junk must not queue on the lock ahead of real shards
        if (result.ResultType != RetrieveShardResultType.Complete)
        {
            throw new OdinClientException("Invalid result type");
        }

        var started = await GetStatusRecordInternal();
        if (!IsCollecting(started))
        {
            throw NoRecoveryUnderWay(noRecoveryCode);
        }

        package ??= await GetDealerPackageAsync(odinContext);
        var envelope = result.Shard?.Player?.OdinId != player
            ? null
            : package?.Envelopes.SingleOrDefault(e => e.ShardId == result.Shard.Id && e.Player.OdinId == player);
        if (envelope == null)
        {
            throw new OdinSecurityException($"{player} did not hold shard {result.Shard?.Id}");
        }

        // A gated friend's browser or an automatic holder sealed it to the recovering browser.  Sealed
        // to another key, it is for an earlier start: that holder's link won't do any more.
        if (!ZkSeal.IsSealedTo(result.Shard.DealerEncryptedShard, started.RecoveryPublicKeyJwk))
        {
            throw new OdinClientException("The shard is not sealed to the recovering browser", OdinClientErrorCode.ShardLinkNoLongerValid);
        }

        // One at a time: each counts the shards in to decide the threshold, and two clicks at once
        // would each see one short of it
        await using var collecting = await LockOrSayBusyAsync(CollectLock, CollectLockWait, "collecting a shard");

        odinContext = OdinContextUpgrades.UpgradeToByPassAclCheck(
            WellKnownAppDrives.ShardRecoveryDrive,
            DrivePermission.Read,
            odinContext);

        await using var tx = await _db.BeginStackedTransactionAsync();

        // Again inside the transaction, as stored: the recovery may have ended, or started over with
        // another key.  The status is written back below only while it is still these bytes, so no
        // exit or start in between is undone, and the shard goes back with the transaction.
        var storedStatus = await Storage.GetBytesAsync(_keyValueTable, ShamirStatusStorageId);
        var status = ReadStatus(storedStatus);
        if (!IsCollecting(status) || status.RecoveryPublicKeyJwk != started.RecoveryPublicKeyJwk)
        {
            throw NoRecoveryUnderWay(noRecoveryCode);
        }

        await _playerShardCollector.SavePlayerShard(result.Shard, odinContext);

        // Only this deal's shards count, as in the status's slots.  Told once, at the threshold; later
        // shards are only ones the browser may need.
        var shardsIn = ShardsIn(package, await CollectedShardIdsAsync(odinContext));
        var reached = status.State == ShamirRecoveryState.AwaitingSufficientDelegateConfirmation && shardsIn >= package.MinMatchingShards;
        var written = status with
        {
            Updated = UnixTimeUtc.Now(),
            State = reached ? ShamirRecoveryState.AwaitingRecoveringBrowser : status.State
        };
        if (!await Storage.TryReplaceAsync(_keyValueTable, ShamirStatusStorageId, storedStatus, written))
        {
            throw NoRecoveryUnderWay(noRecoveryCode);
        }

        if (reached)
        {
            // The dealer can't open the shards: the browser that started recovery rebuilds the secret
            // (GetRecoveryBundleAsync).  The email carries nothing.
            await _recoveryNotifier.EnqueueShardsCollectedEmail(await _passwordKeyRecoveryService.GetRecoveryEmailAsync());

            await _mediator.Publish(new ShamirPasswordRecoverySufficientShardsCollectedNotification()
            {
                OdinContext = odinContext
            });
        }
        else if (status.State == ShamirRecoveryState.AwaitingSufficientDelegateConfirmation)
        {
            // collect the shards, so I can piece together my password
            int remainingRequired = package.MinMatchingShards - shardsIn;
            await _mediator.Publish(new ShamirPasswordRecoveryShardCollectedNotification
            {
                OdinContext = odinContext,
                Sender = result.Shard.Player.OdinId,
                AdditionalMessage = $"You need {remainingRequired} more shards to recover your identity."
            });
        }

        tx.Commit();
    }

    /// <summary>
    /// Recovery with a server-made deal (dealt before the browser dealt every deal; the next password
    /// login deals it again): the browser that started rebuilt the deal's secret from the bundle and
    /// sends it with a new password.  The secret opens only today's master key, so the reset forgets
    /// the zero-knowledge keys, as every reset with it does.
    /// </summary>
    public async Task FinalizeServerMadeDealAsync(byte[] secret, PasswordReply passwordReply, IOdinContext odinContext)
    {
        // One transaction, and recovery mode ends last: a failure leaves the collected shards as they
        // were, so the owner can try again.
        await using var tx = await _db.BeginStackedTransactionAsync();

        var status = await GetStatusRecordInternal();
        if (status?.State != ShamirRecoveryState.AwaitingRecoveringBrowser)
        {
            throw new OdinClientException("Not enough shards are in yet", OdinClientErrorCode.RecoveryNotReady);
        }

        if (!await _configurationService.IsServerMadeAutomatedDealAsync(await GetDealerPackageOrFailAsync(odinContext)))
        {
            throw new OdinClientException("This deal gives back the password in the browser", OdinClientErrorCode.ArgumentError);
        }

        using var distributionKey = secret.ToSensitiveByteArray();
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

        using var timeout = new CancellationTokenSource(HolderAskTimeout);
        var client = await CreateClientAsyncWithToken(sender.GetValueOrDefault(), null, odinContext);
        ApiResponse<HttpContent> response;
        try
        {
            response = await client.SendPlayerShard(new RetrieveShardResult
            {
                ResultType = RetrieveShardResultType.Complete,
                Shard = shard with { Index = index, DealerEncryptedShard = sealedShard }
            }, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _logger.LogWarning("Dealer {dealer} did not answer for shard {shardId} in time", odinId, shardId);
            throw new OdinClientException($"{odinId} didn't answer in time; approve again in a moment", OdinClientErrorCode.RecoveryBusy);
        }

        // the dealer refuses a shard from a delegate it is no longer connected to (#1885); say so,
        // and keep the request so the approval can be retried
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Dealer {dealer} did not accept shard {shardId}: {status}", odinId, shardId, response.StatusCode);
            var code = response.ErrorCode();
            if (code == OdinClientErrorCode.RecoveryBusy)
            {
                throw new OdinClientException($"{odinId} is busy just now; approve again in a moment", OdinClientErrorCode.RecoveryBusy);
            }

            if (code == OdinClientErrorCode.ShardLinkNoLongerValid)
            {
                // For a recovery that ended or started over: nothing to approve any more
                await _approvalCollector.DeleteRequest(request, odinContext);
                throw new OdinClientException($"{odinId}'s recovery has ended or started over; this request is gone",
                    OdinClientErrorCode.ShardLinkNoLongerValid);
            }

            throw new OdinClientException(
                $"{odinId} did not accept the shard ({(int)response.StatusCode}). You may no longer be connected to them.");
        }

        await _approvalCollector.DeleteRequest(request, odinContext);
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

        await _approvalCollector.DeleteRequest(request, odinContext);
    }

    /// <summary>
    /// What the browser that started recovery rebuilds the secret from, sealed to its throwaway key,
    /// once enough shards are in.  No login: only that browser opens it.
    /// </summary>
    public async Task<ShamirRecoveryBundleResponse> GetRecoveryBundleAsync(IOdinContext odinContext)
    {
        var status = await GetStatusRecordInternal();
        if (status?.State != ShamirRecoveryState.AwaitingRecoveringBrowser)
        {
            throw new OdinClientException("Not enough shards are in yet", OdinClientErrorCode.RecoveryNotReady);
        }

        var package = await GetDealerPackageOrFailAsync(odinContext);
        var copies = await GetRecoveryCopiesOrFailAsync(package);

        // Deals made on the server before this kept each envelope's position (0-based), not the piece's
        // x, which is the position plus one: a 0 says so, as no x is 0.  Given as x, so the browser
        // always rebuilds with the dealer's own index.
        var envelopes = package.Envelopes.Any(e => e.Index == 0)
            ? package.Envelopes.Select(e => e with { Index = e.Index + 1 }).ToList()
            : package.Envelopes;

        var bundle = new ShamirRecoveryBundle
        {
            MinMatchingShards = package.MinMatchingShards,
            Envelopes = envelopes,
            Shards = await _playerShardCollector.GetCollectShards(odinContext),
            Copies = copies
        };

        var json = OdinSystemSerializer.Serialize(bundle).ToUtf8ByteArray();
        return new ShamirRecoveryBundleResponse { SealedBundle64 = ZkSeal.Seal(json, status.RecoveryPublicKeyJwk).ToBase64() };
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

    /// <summary>
    /// The deal's copies, which give back the password; null for a server-made automated deal, whose
    /// secret opens today's master key instead.  Refused for any other deal without copies (a reset
    /// forgot them, or an old server-made friend deal, whose gated friends can't release): it can give
    /// nothing back.
    /// </summary>
    private async Task<ZkShamirRecoveryCopies> GetRecoveryCopiesOrFailAsync(DealerShardPackage package)
    {
        if (await _configurationService.IsServerMadeAutomatedDealAsync(package))
        {
            return null;
        }

        return await _zeroKnowledgeKeyService.GetShamirRecoveryCopiesAsync()
               ?? throw new OdinClientException("Password recovery is not set up", OdinClientErrorCode.PasswordRecoveryNotConfigured);
    }

    /// <summary>Whether <paramref name="response"/> is the holder's own refusal of a link.</summary>
    private static bool HolderRefused(IApiResponse response) =>
        response?.StatusCode == HttpStatusCode.BadRequest && response.ErrorCode() == OdinClientErrorCode.ShardLinkNoLongerValid;

    /// <summary>How many of <paramref name="package"/>'s shards are in: the threshold and the status count the same.</summary>
    private static int ShardsIn(DealerShardPackage package, HashSet<Guid> collected) =>
        package.Envelopes.Count(e => collected.Contains(e.ShardId));

    /// <summary>Which shards are in, as recovery reads them with no login.</summary>
    private async Task<HashSet<Guid>> CollectedShardIdsAsync(IOdinContext odinContext) =>
        (await _playerShardCollector.GetCollectShards(odinContext)).Select(c => c.Id).ToHashSet();

    /// <summary>The dealer package, read as recovery reads it with no login; null when there is none.</summary>
    private async Task<DealerShardPackage> GetDealerPackageAsync(IOdinContext odinContext) =>
        await _configurationService.GetDealerShardPackage(
            OdinContextUpgrades.UpgradeToByPassAclCheck(WellKnownAppDrives.ShardRecoveryDrive, DrivePermission.Read, odinContext));

    /// <summary>The dealer package, as <see cref="GetDealerPackageAsync"/>; refused when there is none.</summary>
    private async Task<DealerShardPackage> GetDealerPackageOrFailAsync(IOdinContext odinContext) =>
        await GetDealerPackageAsync(odinContext)
        ?? throw new OdinClientException("Password recovery not configured", OdinClientErrorCode.PasswordRecoveryNotConfigured);

    /// <summary>
    /// Whether a recovery is under way: shards are being collected, or are in and the browser may still
    /// need more.  (The two retired states are stored only without a key, so <see cref="ReadStatus"/>
    /// already reads them as none.)
    /// </summary>
    private static bool IsCollecting(ShamirRecoveryStatusRecord status) =>
        status?.State is ShamirRecoveryState.AwaitingSufficientDelegateConfirmation or ShamirRecoveryState.AwaitingRecoveringBrowser;

    private async Task<ShamirRecoveryStatusRecord> GetStatusRecordInternal() =>
        ReadStatus(await Storage.GetBytesAsync(_keyValueTable, ShamirStatusStorageId));

    /// <summary>
    /// The status as stored.  Every start records the browser's key now: a status without one (an
    /// automated recovery, or one waiting on its finalize link, before this release) can't go on, so it
    /// is no recovery; a new start replaces it, and a fresh entry clears its shards.
    /// </summary>
    private static ShamirRecoveryStatusRecord ReadStatus(byte[] stored)
    {
        var record = SingleKeyValueStorage.Deserialize<ShamirRecoveryStatusRecord>(stored);
        return record?.RecoveryPublicKeyJwk == null ? null : record;
    }

    /// <summary>
    /// No recovery is collecting.  For a holder, <c>ShardLinkNoLongerValid</c>: the recovery ended or
    /// started over, so its link or request won't do any more.
    /// </summary>
    private static OdinClientException NoRecoveryUnderWay(OdinClientErrorCode code = OdinClientErrorCode.ShardLinkNoLongerValid) =>
        new("There is no recovery under way", code);

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
        // No lock: a collect that read the status before writes it back only if it is still there
        await Storage.DeleteAsync(_keyValueTable, ShamirStatusStorageId);

        var drive = await _driveManager.GetDriveAsync(WellKnownAppDrives.ShardRecoveryDrive.Alias, false);
        if (null != drive)
        {
            await _playerShardCollector.DeleteCollectedShards(odinContext);
        }
    }

}