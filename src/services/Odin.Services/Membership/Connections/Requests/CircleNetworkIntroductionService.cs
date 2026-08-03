using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.Push;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.Circles;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Util;
using Refit;

namespace Odin.Services.Membership.Connections.Requests;

/// <summary>
/// Enables introducing identities to each other
/// </summary>
public class CircleNetworkIntroductionService : PeerServiceBase,
    INotificationHandler<ConnectionFinalizedNotification>,
    INotificationHandler<ConnectionBlockedNotification>,
    INotificationHandler<ConnectionDeletedNotification>,
    INotificationHandler<ConnectionRequestReceivedNotification>
{
    private readonly TenantContext _tenantContext;
    private readonly CircleNetworkRequestService _circleNetworkRequestService;
    private readonly ILogger<CircleNetworkIntroductionService> _logger;
    private readonly IMediator _mediator;
    private readonly PeerOutbox _peerOutbox;
    private readonly IdentityDatabase _db;
    private readonly PushNotificationService _pushNotificationService;
    private readonly IDriveManager _driveManager;
    private readonly TenantConfigService _tenantConfigService;
    private readonly VersionUpgradeScheduler _versionUpgradeScheduler;
    private readonly ILifetimeScope _lifetimeScope;

    /// <summary>
    /// Enables introducing identities to each other
    /// </summary>
    public CircleNetworkIntroductionService(OdinConfiguration odinConfiguration,
        CircleNetworkService circleNetworkService,
        CircleNetworkRequestService circleNetworkRequestService,
        ILogger<CircleNetworkIntroductionService> logger,
        IOdinHttpClientFactory odinHttpClientFactory,
        FileSystemResolver fileSystemResolver,
        IMediator mediator,
        PeerOutbox peerOutbox,
        PushNotificationService pushNotificationService,
        IDriveManager driveManager,
        TenantContext tenantContext,
        TenantConfigService tenantConfigService,
        VersionUpgradeScheduler versionUpgradeScheduler,
        IdentityDatabase db,
        ILifetimeScope lifetimeScope)
        : base(odinHttpClientFactory, circleNetworkService, fileSystemResolver, odinConfiguration)
    {
        _circleNetworkRequestService = circleNetworkRequestService;
        _logger = logger;
        _mediator = mediator;
        _peerOutbox = peerOutbox;
        _db = db;
        _pushNotificationService = pushNotificationService;
        _driveManager = driveManager;
        _tenantContext = tenantContext;
        _tenantConfigService = tenantConfigService;
        _versionUpgradeScheduler = versionUpgradeScheduler;
        _lifetimeScope = lifetimeScope;
    }

    private const string ReceivedIntroductionContextKey = "f2f5c94c-c299-4122-8aa2-744d91f3b12f";

    private static readonly ThreeKeyValueStorage ReceivedIntroductionValueStorage =
        TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(ReceivedIntroductionContextKey));

    private static readonly byte[] ReceivedIntroductionDataType = Guid.Parse("0b844f10-9580-4cef-82e6-45b21eb40f62").ToByteArray();


    /// <summary>
    /// Introduces a group of identities to each other
    /// </summary>
    public async Task<IntroductionResult> SendIntroductions(IntroductionGroup group, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendIntroductions);

        OdinValidationUtils.AssertNotNull(group, nameof(group));
        OdinValidationUtils.AssertValidRecipientList(group.Recipients, allowEmpty: false);

        var driveId = SystemDriveConstants.TransientTempDrive.Alias;

        async Task<bool> EnqueueOutboxItem(OdinId recipient, Introduction introduction)
        {
            try
            {
                OdinValidationUtils.AssertNotNull(introduction, nameof(introduction));
                OdinValidationUtils.AssertValidRecipientList(introduction.Identities, allowEmpty: false);

                var clientAuthToken = await ResolveClientAccessTokenAsync(recipient, odinContext, false);

                if (clientAuthToken == null)
                {
                    // Diagnostic: figure out why the token is null. Re-read the ICR with overrideHack
                    // so the read itself can't fail on permissions, then re-read again to see if a
                    // second read returns different data (cache vs db divergence).
                    var icr1 = await CircleNetworkService.GetIcrAsync(recipient, odinContext, overrideHack: true);
                    var icr2 = await CircleNetworkService.GetIcrAsync(recipient, odinContext, overrideHack: true);
                    _logger.LogError(
                        "EnqueueOutboxItem: null clientAuthToken for [{recipient}]. " +
                        "icr1.Status={s1} icr1.LastUpdated={u1} icr1.IsConnected={c1} icr1.HasAccessGrant={g1} " +
                        "icr2.Status={s2} icr2.LastUpdated={u2} icr2.IsConnected={c2} icr2.HasAccessGrant={g2} " +
                        "tenant={tenant}",
                        recipient,
                        icr1?.Status, icr1?.LastUpdated, icr1?.IsConnected(), icr1?.PeerKeyStore != null,
                        icr2?.Status, icr2?.LastUpdated, icr2?.IsConnected(), icr2?.PeerKeyStore != null,
                        odinContext.Tenant);
                    return false;
                }

                var item = new OutboxFileItem
                {
                    Recipient = recipient,
                    Priority = 50, //super high priority to ensure these are sent quickly,
                    Type = OutboxItemType.SendIntroduction,
                    AttemptCount = 0,
                    File = new InternalDriveFileId()
                    {
                        DriveId = driveId,
                        FileId = recipient.ToHashId() //SequentialGuid.CreateGuid()
                    },
                    DependencyFileId = default,
                    State = new OutboxItemState
                    {
                        TransferInstructionSet = null,
                        OriginalTransitOptions = null,
                        EncryptedClientAuthToken = clientAuthToken.ToPortableBytes(),
                        Data = OdinSystemSerializer.Serialize(introduction).ToUtf8ByteArray()
                    },
                };

                await _peerOutbox.AddItemAsync(item, useUpsert: true);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to enqueue introduction for recipient: [{recipient}]", recipient);
                return false;
            }

            return true;
        }

        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendIntroductions);

        OdinValidationUtils.AssertNotNull(group, nameof(group));
        OdinValidationUtils.AssertValidRecipientList(group.Recipients, allowEmpty: false);

        var recipients = group.Recipients.ToOdinIdList().Without(odinContext.Tenant);
        // var bytes = ByteArrayUtil.Combine(recipients.Select(i => i.ToByteArray()).ToArray());
        // group.Signature = Sign(bytes, odinContext);


        var result = new IntroductionResult();
        foreach (var recipient in recipients)
        {
            var introduction = new Introduction
            {
                Message = group.Message,
                Identities = recipients.ToDomainNames(),
                Timestamp = UnixTimeUtc.Now()
            };

            result.RecipientStatus[recipient] = await EnqueueOutboxItem(recipient, introduction);
        }

        var failed = result.RecipientStatus.Where(kvp => !kvp.Value).Select(kvp => kvp.Key).ToList();
        if (failed.Count > 0)
        {
            _logger.LogWarning("Introduction send: {failedCount} of {total} recipient(s) could not be enqueued: {failed}",
                failed.Count, result.RecipientStatus.Count, string.Join(", ", failed));
        }
        else
        {
            _logger.LogDebug("Introduction send: enqueued for all {total} recipient(s)", result.RecipientStatus.Count);
        }

        return result;
    }

    /// <summary>
    /// Reports whether this server is in a state where it could accept an introduction
    /// from the calling identity. Used by the peer preflight endpoint. Does not throw on
    /// permission checks — just reports the result so the caller can render a meaningful
    /// message to its user.
    /// </summary>
    public async Task<PeerIntroductionPreflightResponse> PreflightIncomingIntroductionAsync(IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsAuthenticated();

        var isConfigured = await _tenantConfigService.IsIdentityServerConfiguredAsync();

        bool requiresUpgrade = false;
        try
        {
            (requiresUpgrade, _, _) = await _versionUpgradeScheduler.RequiresUpgradeAsync();
        }
        catch (Exception ex)
        {
            // Config storage can fail on a partially-initialized server. Treat this as
            // "no signal" rather than failing the whole preflight; the IsConfigured flag
            // already tells the caller setup isn't complete.
            //
            // Warning, not Debug: this swallows an exception AND silently answers requiresUpgrade=false,
            // so at production log levels the failure left no trace while still changing what we told
            // the caller. Same treatment as the connection-state lookup below.
            _logger.LogWarning(ex, "Preflight incoming: RequiresUpgradeAsync threw; reporting no upgrade signal");
        }

        // The Confirmed Connections circle is what carries AllowIntroductions, so a false there conflates
        // "I revoked you", "I never confirmed you", and "I don't know you at all". Report the connection
        // and confirmation state alongside it so the caller can tell those apart.
        var isCallerConnected = odinContext.Caller.IsConnected;

        var callerCircles = odinContext.Caller.Circles?.ToList();
        var isCallerConfirmed = isCallerConnected &&
                                (callerCircles?.Any(c => c == SystemCircleConstants.ConfirmedConnectionsCircleId) ?? false);

        // Needed to tell "never confirmed" from "confirmed and then revoked": both leave the caller out of
        // Confirmed Connections, but only the former is still in Auto-connected.
        var isCallerAutoConnected = isCallerConnected &&
                                    (callerCircles?.Any(c => c == SystemCircleConstants.AutoConnectionsCircleId) ?? false);

        var allowsIntroductions = CallerMayIntroduce(odinContext, isCallerAutoConnected);

        var connectionState = isCallerConnected
            ? PeerCallerConnectionState.Connected
            : await ResolveCallerConnectionStateAsync(odinContext);

        var caller = odinContext.Caller.OdinId;

        if (allowsIntroductions)
        {
            // Information, not Debug: a preflight that succeeds is the only evidence that the
            // auto-accept path in CallerMayIntroduce fired, and at production log levels a Debug line
            // would make "now permitted" indistinguishable from "never asked". The circle flags say
            // which branch permitted it -- confirmed carries the grant, auto-connected does not.
            _logger.LogInformation(
                "Preflight incoming: permitting introductions from {caller}. isCallerConnected={isCallerConnected} " +
                "isCallerConfirmed={isCallerConfirmed} isCallerAutoConnected={isCallerAutoConnected}",
                caller, isCallerConnected, isCallerConfirmed, isCallerAutoConnected);
        }
        else
        {
            // The whole point of the extra fields: log why we are about to say no, so the reason
            // distribution is visible in production rather than collapsing into one message.
            //
            // disableAutoAcceptConnectionRequests is this identity's own setting, not the caller's, and
            // it is the other half of the auto-connected branch in CallerMayIntroduce. Without it,
            // isCallerAutoConnected=true on a refusal is unexplainable from the log alone: it is only
            // possible when this flag is true, and reading it here beats inferring it from the reason.
            _logger.LogInformation(
                "Preflight incoming: not permitting introductions from {caller}. reason={reason} " +
                "isConfigured={isConfigured} requiresUpgrade={requiresUpgrade} isCallerConnected={isCallerConnected} " +
                "isCallerConfirmed={isCallerConfirmed} isCallerAutoConnected={isCallerAutoConnected} " +
                "disableAutoAcceptConnectionRequests={disableAutoAcceptConnectionRequests} " +
                "usingDefaultSettings={usingDefaultSettings} tenant={tenant} " +
                "tenantContextInstance={tenantContextInstance} version={version} " +
                "connectionState={connectionState}",
                caller,
                DescribeIncomingRefusal(isConfigured, requiresUpgrade, isCallerConnected, isCallerConfirmed,
                    isCallerAutoConnected, connectionState),
                isConfigured, requiresUpgrade, isCallerConnected, isCallerConfirmed, isCallerAutoConnected,
                _tenantContext.Settings.DisableAutoAcceptConnectionRequests,
                _tenantContext.IsUsingDefaultSettings,
                odinContext.Tenant,
                RuntimeHelpers.GetHashCode(_tenantContext),
                Version.VersionText,
                connectionState);
        }

        return new PeerIntroductionPreflightResponse
        {
            IsConfigured = isConfigured,
            RequiresUpgrade = requiresUpgrade,
            AllowsIntroductions = allowsIntroductions,
            IsCallerConnected = isCallerConnected,
            IsCallerConfirmed = isCallerConfirmed,
            IsCallerAutoConnected = isCallerAutoConnected,
            CallerConnectionState = connectionState,
        };
    }

    /// <summary>
    /// Whether the calling identity is allowed to introduce others to us.
    ///
    /// <para>
    /// The <see cref="PermissionKeys.AllowIntroductions"/> grant itself comes from the Confirmed
    /// Connections circle. On top of that, an identity that auto-accepts connection requests
    /// (<see cref="TenantSettings.DisableAutoAcceptConnectionRequests"/> is false) has already decided it
    /// will connect to whoever asks, so there is nothing left for it to withhold from the identities it
    /// auto-connected -- treating them as unable to introduce made every auto-connection a dead end until
    /// the owner confirmed it by hand.
    /// </para>
    ///
    /// <para>
    /// This is deliberately a policy check rather than a grant added to
    /// <see cref="SystemCircleConstants.AutoConnectionsSystemCircleDefinition"/>: the condition is a
    /// per-tenant setting, and evaluating it here takes effect immediately for every existing
    /// auto-connection instead of requiring each member's stored circle grant to be re-issued (which
    /// needs the owner's master key). The trade-off is that it does not surface in the circle definition
    /// or in GetConnectionInfo -- the permission is not really in the caller's
    /// <see cref="CircleGrant.PermissionSet"/>.
    /// </para>
    /// </summary>
    /// <param name="isCallerAutoConnected">
    /// Whether the caller is a member of the Auto-connected circle. Passed in because callers have
    /// usually already computed it.
    /// </param>
    private bool CallerMayIntroduce(IOdinContext odinContext, bool isCallerAutoConnected)
    {
        if (odinContext.PermissionsContext?.HasPermission(PermissionKeys.AllowIntroductions) ?? false)
        {
            return true;
        }

        return isCallerAutoConnected && !_tenantContext.Settings.DisableAutoAcceptConnectionRequests;
    }

    /// <summary>
    /// Whether the caller holds a connected context that sits in the Auto-connected circle.
    /// </summary>
    private static bool IsCallerAutoConnected(IOdinContext odinContext)
    {
        return odinContext.Caller.IsConnected &&
               (odinContext.Caller.Circles?.Any(c => c == SystemCircleConstants.AutoConnectionsCircleId) ?? false);
    }

    /// <summary>
    /// A short, greppable label for why this server is about to report that it will not accept an
    /// introduction. Mirrors the classification the caller will apply to the same fields.
    /// </summary>
    private static string DescribeIncomingRefusal(bool isConfigured, bool requiresUpgrade, bool isCallerConnected,
        bool isCallerConfirmed, bool isCallerAutoConnected, PeerCallerConnectionState connectionState)
    {
        if (!isConfigured)
        {
            return "not-configured";
        }

        if (requiresUpgrade)
        {
            return "requires-upgrade";
        }

        if (connectionState == PeerCallerConnectionState.NeedsRepair)
        {
            return "connection-needs-repair";
        }

        if (!isCallerConnected)
        {
            return "caller-not-recognized";
        }

        // Only reachable when this identity does NOT auto-accept connection requests -- otherwise
        // CallerMayIntroduce would have let an auto-connected caller through and we would not be here.
        if (isCallerAutoConnected && !isCallerConfirmed)
        {
            return "auto-connection-not-confirmed";
        }

        return "permission-not-granted";
    }

    /// <summary>
    /// Works out why this request did not resolve a connected context. OdinContextMiddleware swallows the
    /// distinction -- it catches the remote-ICR security exception, sets the RemoteServerIcrIssue header
    /// and falls back to the public transit context -- so re-read the caller's ICR to recover it.
    /// </summary>
    private async Task<PeerCallerConnectionState> ResolveCallerConnectionStateAsync(IOdinContext odinContext)
    {
        var caller = odinContext.Caller.OdinId;
        if (!caller.HasValue)
        {
            return PeerCallerConnectionState.Unknown;
        }

        try
        {
            // Same shape as VerifyConnection's caller lookup: overrideHack because this is a peer context
            // (the caller is the remote identity, not our owner) and tryUpgradeEncryption because the
            // upgrade path needs our own ICR key, which is not in scope here.
            var icr = await CircleNetworkService.GetIcrAsync(caller.Value, odinContext,
                overrideHack: true, tryUpgradeEncryption: false);

            if (icr == null)
            {
                return PeerCallerConnectionState.NotRecognized;
            }

            // A connected ICR whose key store is unusable is a repairable fault, not a decision. Note the
            // deliberate merge on the other branch: blocked callers land in NotRecognized along with
            // unknown ones, so that preflight cannot be used to detect a block.
            if (icr.IsConnected())
            {
                return (icr.PeerKeyStore?.IsValid() ?? false)
                    ? PeerCallerConnectionState.Connected
                    : PeerCallerConnectionState.NeedsRepair;
            }

            return PeerCallerConnectionState.NotRecognized;
        }
        catch (Exception ex)
        {
            // A partially-initialized server can fail this lookup; report no signal rather than guessing.
            _logger.LogWarning(ex, "Preflight incoming: could not resolve connection state for caller {caller}; " +
                                   "reporting Unknown", caller);
            return PeerCallerConnectionState.Unknown;
        }
    }

    /// <summary>
    /// Probes each recipient to determine whether an introduction would succeed before
    /// the caller commits to <see cref="SendIntroductions"/>. Runs probes in parallel and
    /// returns a per-recipient status the UI can render to the user.
    /// </summary>
    public async Task<IntroductionPreflightResult> PreflightIntroductionsAsync(
        List<string> recipients,
        IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendIntroductions);
        OdinValidationUtils.AssertValidRecipientList(recipients, allowEmpty: false);

        // Note: our own identity is dropped silently, so the result can contain fewer entries than were
        // requested. Callers must match results by recipient rather than by position or count.
        var targets = recipients.ToOdinIdList().Without(odinContext.Tenant);

        // Information, not Debug: this is the only line that marks a preflight having been requested at
        // all. Without it, a run where every recipient comes back Ready is indistinguishable from one
        // that never happened.
        _logger.LogInformation("Preflight: probing {count} recipient(s)", targets.Count());

        var probeTasks = targets.Select(r => ProbeRecipientAsync(r, odinContext, cancellationToken)).ToList();
        var statuses = await Task.WhenAll(probeTasks);

        var notReady = statuses.Where(s => s.Status != IntroductionPreflightStatus.Ready).ToList();
        if (notReady.Count > 0)
        {
            _logger.LogInformation("Preflight: {notReadyCount} of {total} recipient(s) not ready: {breakdown}",
                notReady.Count,
                statuses.Length,
                string.Join(", ", notReady.Select(s => $"{s.Recipient}={s.Status}")));
        }

        return new IntroductionPreflightResult
        {
            Recipients = statuses.ToList()
        };
    }

    /// <summary>
    /// Wraps the probe so every outcome is logged in one place, with the fields the recipient reported.
    /// This is the log line to read when diagnosing why a specific recipient came back not-ready: it holds
    /// the recipient's own answer, so the sender's logs alone are enough -- no access to the recipient's
    /// server required.
    /// </summary>
    private async Task<RecipientPreflightStatus> ProbeRecipientAsync(OdinId recipient, IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        var status = await ProbeRecipientInternalAsync(recipient, odinContext, cancellationToken);

        if (status.Status == IntroductionPreflightStatus.Ready)
        {
            // Mirrors the recipient's own permitting line at the same level, so the sender's log alone
            // shows which side of the auto-connected/confirmed split let the introduction through.
            _logger.LogInformation(
                "Preflight: {recipient} is ready. isCallerConfirmed={isCallerConfirmed} " +
                "isCallerAutoConnected={isCallerAutoConnected}",
                recipient, status.IsCallerConfirmed, status.IsCallerAutoConnected);
            return status;
        }

        _logger.LogInformation(
            "Preflight: {recipient} is not ready. status={status} remedyActor={remedyActor} transient={transient} " +
            "isConfigured={isConfigured} requiresUpgrade={requiresUpgrade} allowsIntroductions={allowsIntroductions} " +
            "isCallerConnected={isCallerConnected} isCallerConfirmed={isCallerConfirmed} " +
            "isCallerAutoConnected={isCallerAutoConnected} connectionState={connectionState} detail={detail}",
            recipient,
            status.Status,
            status.RemedyActor,
            status.IsTransient,
            status.IsConfigured,
            status.RequiresUpgrade,
            status.AllowsIntroductions,
            status.IsCallerConnected,
            status.IsCallerConfirmed,
            status.IsCallerAutoConnected,
            status.CallerConnectionState,
            status.Detail);

        return status;
    }

    private async Task<RecipientPreflightStatus> ProbeRecipientInternalAsync(OdinId recipient, IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        var status = new RecipientPreflightStatus
        {
            Recipient = recipient,
        };

        try
        {
            // Each probe runs in parallel (see PreflightIntroductionsAsync), so it must use its own IOC
            // scope. Resolving DB-touching services from the shared request scope would have multiple
            // probes contend on the same ScopedConnectionFactory and trip its parallelism guard.
            await using var childScope = _lifetimeScope.BeginLifetimeScope($"ProbeRecipient:{Guid.NewGuid()}");

            ClientAccessToken clientAuthToken;
            try
            {
                clientAuthToken = await ResolveClientAccessTokenScopedAsync(childScope, recipient, odinContext);
            }
            catch (Exception ex)
            {
                // We hold an ICR but cannot turn it into a usable token (e.g. the stored CAT will not
                // decrypt under the current ICR key). That is our fault, not the recipient's, and it used
                // to land in the outer catch as UnknownError with a raw exception string.
                _logger.LogWarning(ex, "Preflight: local ICR with {recipient} did not yield a client access token", recipient);
                status.Status = IntroductionPreflightStatus.SenderConnectionInvalid;
                status.Detail = $"Local connection record is present but unusable: {ex.Message}";
                return status;
            }

            if (clientAuthToken == null)
            {
                status.Status = IntroductionPreflightStatus.NotConnected;
                status.Detail = "No valid ICR with recipient";
                return status;
            }

            var client = await OdinHttpClientFactory.CreateClientUsingAccessTokenAsync<ICircleNetworkPeerConnectionsClient>(
                recipient,
                clientAuthToken.ToAuthenticationToken());

            ApiResponse<PeerIntroductionPreflightResponse> response;
            try
            {
                response = await client.PreflightIntroduction(cancellationToken);
            }
            catch (TaskCanceledException)
            {
                status.Status = IntroductionPreflightStatus.RecipientTimedOut;
                status.Detail = "Recipient did not respond";
                return status;
            }
            catch (HttpRequestException ex)
            {
                (status.Status, status.Detail) = ClassifyTransportFailure(ex);
                return status;
            }

            if (!response.IsSuccessStatusCode || response.Content == null)
            {
                (status.Status, status.Detail) = ClassifyErrorResponse(response);
                return status;
            }

            var payload = response.Content;
            status.IsConfigured = payload.IsConfigured;
            status.RequiresUpgrade = payload.RequiresUpgrade;
            status.AllowsIntroductions = payload.AllowsIntroductions;
            status.IsCallerConnected = payload.IsCallerConnected;
            status.IsCallerConfirmed = payload.IsCallerConfirmed;
            status.IsCallerAutoConnected = payload.IsCallerAutoConnected;
            status.CallerConnectionState = payload.CallerConnectionState;

            if (!payload.IsConfigured)
            {
                status.Status = IntroductionPreflightStatus.RecipientNotConfigured;
                status.Detail = "Recipient identity server has not completed initial setup";
                return status;
            }

            if (payload.RequiresUpgrade)
            {
                status.Status = IntroductionPreflightStatus.RecipientRequiresUpgrade;
                status.Detail = "Recipient identity server requires a version upgrade";
                return status;
            }

            if (!payload.AllowsIntroductions)
            {
                (status.Status, status.Detail) = ClassifyMissingPermission(payload);
                return status;
            }

            status.Status = IntroductionPreflightStatus.Ready;
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preflight: probe to {recipient} failed unexpectedly", recipient);
            status.Status = IntroductionPreflightStatus.UnknownError;
            status.Detail = ex.Message;
            return status;
        }
    }

    /// <summary>
    /// Explains a <c>AllowsIntroductions == false</c>. The permission comes from the recipient's Confirmed
    /// Connections circle, so on its own the flag says nothing about why -- the connection and confirmation
    /// state the recipient reports alongside it is what separates a pending confirmation from a broken
    /// connection from a deliberate revocation.
    ///
    /// <para>
    /// A recipient that auto-accepts connection requests also permits its auto-connections to introduce
    /// (see <c>CallerMayIntroduce</c>), so it would have reported <c>true</c> and never reached here. The
    /// <see cref="IntroductionPreflightStatus.RecipientConnectionNotConfirmed"/> branch below therefore
    /// now describes a recipient that does not auto-accept.
    /// </para>
    /// </summary>
    private static (IntroductionPreflightStatus status, string detail) ClassifyMissingPermission(
        PeerIntroductionPreflightResponse payload)
    {
        if (payload.CallerConnectionState == PeerCallerConnectionState.NeedsRepair)
        {
            return (IntroductionPreflightStatus.RecipientConnectionNeedsRepair,
                "Recipient has a connection record for this identity but it is not usable");
        }

        if (payload.CallerConnectionState == PeerCallerConnectionState.NotRecognized)
        {
            return (IntroductionPreflightStatus.RecipientDoesNotRecognizeConnection,
                "Recipient has no usable connection record for this identity");
        }

        if (payload.IsCallerConnected)
        {
            // Only an auto-connection that is still in the Auto-connected circle is genuinely "awaiting
            // confirmation". A connection that is in neither system circle was confirmed and then had the
            // circle taken away, which is a decision and belongs under IntroductionsNotPermitted.
            if (payload.IsCallerAutoConnected && !payload.IsCallerConfirmed)
            {
                return (IntroductionPreflightStatus.RecipientConnectionNotConfirmed,
                    "Recipient has not confirmed the connection, so it carries no AllowIntroductions");
            }

            return (IntroductionPreflightStatus.IntroductionsNotPermitted,
                "Recipient is connected but has not granted AllowIntroductions to this identity");
        }

        // CallerConnectionState is Unknown and the recipient did not report being connected: either an
        // older build that predates these fields, or one that could not determine its own state. We cannot
        // tell the cases apart, so fall back to the pre-existing (over-broad) status rather than guess.
        return (IntroductionPreflightStatus.IntroductionsNotPermitted,
            "Recipient did not grant AllowIntroductions and did not report a connection state");
    }

    /// <summary>
    /// Maps a non-success preflight response. Peers signal several distinguishable conditions that were
    /// previously collapsed into <see cref="IntroductionPreflightStatus.Unreachable"/>.
    /// </summary>
    private static (IntroductionPreflightStatus status, string detail) ClassifyErrorResponse(
        ApiResponse<PeerIntroductionPreflightResponse> response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (IntroductionPreflightStatus.PreflightNotSupported,
                "Recipient does not implement the introduction preflight endpoint");
        }

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable &&
            response.Headers.IsTrue(OdinHeaderNames.UpgradeIsRunning))
        {
            return (IntroductionPreflightStatus.RecipientUpgradeInProgress,
                "Recipient identity server is running a version upgrade");
        }

        // A recipient that has never completed setup also sets the ICR-issue header on its way to this
        // error (it cannot build a transit context either), so check the error code first: "finish your
        // setup" is the actionable one, and it is a strictly narrower condition.
        if (response.Error != null && response.Error.TryParseProblemDetails(out var errorCode))
        {
            if (errorCode is OdinClientErrorCode.NotInitialized or OdinClientErrorCode.RecipientIdentityNotConfigured)
            {
                return (IntroductionPreflightStatus.RecipientNotConfigured,
                    "Recipient identity server has not completed initial setup");
            }
        }

        if (response.Headers.IsTrue(HttpHeaderConstants.RemoteServerIcrIssue))
        {
            return (IntroductionPreflightStatus.RecipientDoesNotRecognizeConnection,
                "Recipient could not resolve a connection record for this identity");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (IntroductionPreflightStatus.RecipientRejected, "Recipient denied the preflight");
        }

        return (IntroductionPreflightStatus.Unreachable, $"Recipient returned {(int)response.StatusCode}");
    }

    /// <summary>
    /// Separates the transport failures that used to arrive as a single <c>Unreachable</c> plus a raw
    /// exception message. A dead domain, an expired certificate and a briefly-down server call for
    /// completely different responses from the user.
    /// </summary>
    private static (IntroductionPreflightStatus status, string detail) ClassifyTransportFailure(HttpRequestException ex)
    {
        switch (ex.HttpRequestError)
        {
            case HttpRequestError.NameResolutionError:
                return (IntroductionPreflightStatus.RecipientUnresolvable, "Recipient domain could not be resolved");
            case HttpRequestError.SecureConnectionError:
                return (IntroductionPreflightStatus.RecipientCertificateInvalid, "TLS handshake with recipient failed");
        }

        // HttpRequestError is not always populated (it depends on the handler), so fall back to walking the
        // inner exception chain for the same signals.
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
        {
            if (inner is AuthenticationException)
            {
                return (IntroductionPreflightStatus.RecipientCertificateInvalid, "TLS handshake with recipient failed");
            }

            if (inner is SocketException socketException)
            {
                switch (socketException.SocketErrorCode)
                {
                    case SocketError.HostNotFound:
                    case SocketError.NoData:
                    case SocketError.TryAgain:
                        return (IntroductionPreflightStatus.RecipientUnresolvable, "Recipient domain could not be resolved");
                    case SocketError.ConnectionRefused:
                        return (IntroductionPreflightStatus.RecipientConnectionRefused, "Recipient refused the connection");
                    case SocketError.TimedOut:
                        return (IntroductionPreflightStatus.RecipientTimedOut, "Recipient did not respond");
                }
            }
        }

        return (IntroductionPreflightStatus.Unreachable, ex.Message);
    }

    /// <summary>
    /// Mirrors <see cref="PeerServiceBase.ResolveClientAccessTokenAsync"/> but resolves the
    /// <see cref="CircleNetworkService"/> from the supplied child scope so parallel probes each
    /// use their own ScopedConnectionFactory. Returns null when the recipient is not connected.
    /// </summary>
    private async Task<ClientAccessToken> ResolveClientAccessTokenScopedAsync(
        ILifetimeScope childScope, OdinId recipient, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasAtLeastOnePermission(
            PermissionKeys.UseTransitWrite,
            PermissionKeys.UseTransitRead);

        var circleNetworkService = childScope.Resolve<CircleNetworkService>();

        // overrideHack: we already asserted UseTransitWrite or UseTransitRead above
        var icr = await circleNetworkService.GetIcrAsync(recipient, odinContext, overrideHack: true);
        if (icr.IsConnected() == false)
        {
            return null;
        }

        return icr.CreateClientAccessToken(odinContext.PermissionsContext.GetIcrKey());
    }

    /// <summary>
    /// Stores an incoming introduction
    /// </summary>
    public async Task ReceiveIntroductions(SharedSecretEncryptedPayload payload, IOdinContext odinContext)
    {
        // Deliberately not GetCallerOdinIdOrFail: this is only for the log lines below, and a refusal
        // should report the caller it refused rather than throw a different exception on the way. The
        // call further down still fails hard if there is no caller.
        var caller = odinContext.Caller.OdinId;
        var isCallerAutoConnected = IsCallerAutoConnected(odinContext);

        // Same predicate the preflight endpoint reports on, so a Ready preflight is not followed by a
        // rejected send. Note this must stay a check rather than the plain AssertHasPermission it replaced:
        // an auto-connected caller on an auto-accepting identity is permitted without the permission ever
        // being in their stored grant.
        if (!CallerMayIntroduce(odinContext, isCallerAutoConnected))
        {
            // The refusal that the preflight endpoint predicts, logged where it actually happens. Without
            // it this side is silent: the sender gets a security exception and we record nothing, so a
            // rejected introduction was only visible from the other identity's logs.
            _logger.LogInformation(
                "Rejecting introductions from {caller}. isCallerConnected={isCallerConnected} " +
                "isCallerAutoConnected={isCallerAutoConnected} " +
                "disableAutoAcceptConnectionRequests={disableAutoAcceptConnectionRequests}",
                caller,
                odinContext.Caller.IsConnected,
                isCallerAutoConnected,
                _tenantContext.Settings.DisableAutoAcceptConnectionRequests);

            throw new OdinSecurityException("Does not have permission");
        }

        // Information, not Debug: this is the introduction actually landing, the event the preflight only
        // predicts. Paired with the rejection line above, every incoming introduction now has an outcome
        // in the log at production levels.
        _logger.LogInformation("Receiving introductions from {sender}", caller);

        OdinValidationUtils.AssertNotNull(payload, nameof(payload));

        var payloadBytes = payload.Decrypt(odinContext.PermissionsContext.SharedSecretKey);
        Introduction introduction = OdinSystemSerializer.Deserialize<Introduction>(payloadBytes.ToStringFromUtf8Bytes());

        OdinValidationUtils.AssertNotNull(introduction, nameof(introduction));
        OdinValidationUtils.AssertValidRecipientList(introduction.Identities, allowEmpty: false);

        introduction.Timestamp = UnixTimeUtc.Now();
        var introducer = odinContext.GetCallerOdinIdOrFail();

        var driveId = SystemDriveConstants.TransientTempDrive.Alias;

        //Store the introductions by the identity to which you're being introduces
        var newIdentities = new List<string>();
        var skippedAlreadyConnected = 0;
        var skippedBlocked = 0;
        foreach (var identity in introduction.Identities.ToOdinIdList().Without(odinContext.Tenant))
        {
            // Note: we do not indicate if you're already connected or
            // have blocked the identity being introduced as we do not
            // want to communicate any such information to the introducer.
            // Logging it locally is fine -- these are our own logs, not a response to the introducer.
            var icr = await CircleNetworkService.GetIcrAsync(identity, odinContext, overrideHack: true);
            if (icr.IsConnected() || icr.Status == ConnectionStatus.Blocked)
            {
                if (icr.Status == ConnectionStatus.Blocked)
                {
                    skippedBlocked++;
                }
                else
                {
                    skippedAlreadyConnected++;
                }

                continue;
            }

            var iid = new IdentityIntroduction()
            {
                IntroducerOdinId = introducer,
                Identity = identity,
                Message = introduction.Message,
                Received = UnixTimeUtc.Now()
            };

            await SaveAndEnqueueToConnect(iid, driveId);
            newIdentities.Add(identity);
        }

        _logger.LogInformation(
            "Introduction received from {introducer}: {newCount} new, {alreadyConnected} already connected, " +
            "{blocked} blocked, of {total} introduced identities",
            introducer, newIdentities.Count, skippedAlreadyConnected, skippedBlocked,
            introduction.Identities.Count);

        if (newIdentities.Count == 0)
        {
            // Nothing to do and nothing to notify the owner about. Logged above so a "the introduction
            // arrived but nothing happened" report can be told apart from one that never arrived.
            return;
        }

        var notification = new IntroductionsReceivedNotification()
        {
            IntroducerOdinId = introducer,
            Introduction = new Introduction
            {
                Identities = newIdentities,
                Message = introduction.Message,
                Timestamp = introduction.Timestamp,
            },
            OdinContext = odinContext
        };

        await _pushNotificationService.EnqueueNotification(introducer, new AppNotificationOptions()
            {
                AppId = SystemAppConstants.OwnerAppId,
                TypeId = notification.NotificationTypeId,
                TagId = introducer,
                Silent = false,
                // UnEncryptedJson = OdinSystemSerializer.Serialize(new
                // {
                //     IntroducerOdinId = introducer,
                //     Introduction = introduction,
                // })
            },
            OdinContextUpgrades.UsePermissions(odinContext, PermissionKeys.SendPushNotifications));

        await _mediator.Publish(notification);
    }

    public async Task ForceAutoAcceptEligibleConnectionRequestsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        var incomingConnectionRequests = await _circleNetworkRequestService.GetPendingRequestsAsync(PageOptions.All, odinContext);
        _logger.LogDebug("Running AutoAccept for incomingConnectionRequests ({count} requests)",
            incomingConnectionRequests.Results.Count);

        foreach (PendingConnectionRequestHeader request in incomingConnectionRequests.Results)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("AutoAcceptEligibleConnectionRequests - Cancellation requested; breaking from loop");
                break;
            }

            await AutoAcceptEligibleConnectionRequestAsync(request, force: true, odinContext);
        }
    }

    private async Task AutoAcceptEligibleConnectionRequestAsync(PendingConnectionRequestHeader request, bool force,
        IOdinContext odinContext)
    {
        if (force && !odinContext.Caller.HasMasterKey)
        {
            return;
        }

        if (_tenantContext.Settings.DisableAutoAcceptIntroductionsForTests && !force)
        {
            return;
        }

        var sender = request.SenderOdinId;
        var requiresIcr = request.EccEncryptedPayload.KeyType == PublicPrivateKeyType.OnlineIcrEncryptedKey;
        if (requiresIcr && odinContext.PermissionsContext.GetIcrKey(failIfNotFound: false) == null)
        {
            _logger.LogDebug("Auto Accept attempting to accept connection request from {sender} that is " +
                             "encrypted with OnlineIcrEncryptedKey, however odinContext does not have ICR key " +
                             "available.  Bypassing this request.",
                sender);
            return;
        }

        try
        {
            var newContext = OdinContextUpgrades.UsePermissions(odinContext,
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.ReadConnections);

            var introduction = await this.GetIntroductionInternalAsync(sender);
            if (null != introduction)
            {
                _logger.LogDebug("Auto-accept connection request from {sender} due to received introduction", sender);
                await AutoAcceptAsync(sender, newContext);
                return;
            }

            var existingSentRequest = await _circleNetworkRequestService.GetSentRequestAsync(sender, newContext);
            if (null != existingSentRequest)
            {
                _logger.LogDebug("Auto-accept connection request from {sender} due to an existing outgoing request", sender);
                await AutoAcceptAsync(sender, newContext);
                return;
            }

            if (await CircleNetworkService.IsConnectedAsync(sender, newContext))
            {
                _logger.LogDebug("Auto-accept connection request from {sender} since there is already an ICR", sender);
                await AutoAcceptAsync(sender, newContext);
                return;
            }

            if (!_tenantContext.Settings.DisableAutoAcceptConnectionRequests)
            {
                var pending = await _circleNetworkRequestService.GetPendingRequestAsync(sender, newContext);
                if (pending?.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwnerApp)
                {
                    _logger.LogDebug("Auto-accept app-initiated connection request from {sender}", sender);
                    await AutoAcceptAsync(sender, newContext);
                    return;
                }
            }

            _logger.LogDebug("Auto-accept was not executed for request from {sender}; no matching reasons to accept", sender);
        }
        catch (OdinClientException oce)
        {
            if (oce.ErrorCode == OdinClientErrorCode.IncomingRequestNotFound)
            {
                _logger.LogError(oce, "Failed while trying to auto-accept a connection request from {identity}.  The request was not found",
                    sender);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed while trying to auto-accept a connection request from {identity}", sender);
        }
    }

    /// <summary>
    /// Sends connection requests for introductions
    /// </summary>
    public async Task SendOutstandingConnectionRequestsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        //upgrading for use in a bg process
        var newOdinContext = OdinContextUpgrades.UsePermissions(odinContext, PermissionKeys.ReadCircleMembership);

        //get the introductions from the list
        var introductions = await GetReceivedIntroductionsAsync(newOdinContext);

        _logger.LogDebug("Introduction connect: sending outstanding connection requests to {introductionCount} introduction(s)",
            introductions.Count);

        var processed = 0;
        foreach (var intro in introductions)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("SendOutstandingConnectionRequests - Cancellation requested; breaking from loop");
                break;
            }

            var recipient = intro.Identity;
            processed++;

            // NOTE: both of the conditions below `break` rather than `continue`, so a single skipped
            // introducee abandons every remaining introduction in this batch. The log messages describe a
            // per-recipient skip, which suggests `continue` was intended. Logged loudly rather than changed
            // here, because altering background-worker control flow is out of scope for this change.
            var hasOutstandingRequest = await _circleNetworkRequestService.HasPendingOrSentRequest(recipient, odinContext);
            if (hasOutstandingRequest)
            {
                _logger.LogInformation(
                    "Introduction connect: {recipient} has an incoming or outgoing request; abandoning the " +
                    "remaining {remaining} of {total} outstanding introduction(s) in this batch",
                    recipient, introductions.Count - processed, introductions.Count);
                break;
            }

            var alreadyConnected = await CircleNetworkService.IsConnectedAsync(recipient, odinContext);
            if (alreadyConnected)
            {
                _logger.LogInformation(
                    "Introduction connect: {recipient} is already connected; abandoning the remaining " +
                    "{remaining} of {total} outstanding introduction(s) in this batch",
                    recipient, introductions.Count - processed, introductions.Count);
                break;
            }

            _logger.LogDebug("Introduction connect: sending connection request to {recipient}", recipient);
            await this.SendIntroductoryConnectionRequestInternalAsync(intro, cancellationToken, newOdinContext);
        }
    }

    public async Task<List<IdentityIntroduction>> GetReceivedIntroductionsAsync(IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
        var results = await ReceivedIntroductionValueStorage.GetByCategoryAsync<IdentityIntroduction>(_db.KeyThreeValueCached,
            ReceivedIntroductionDataType);
        return results.ToList();
    }

    public async Task Handle(ConnectionFinalizedNotification notification, CancellationToken cancellationToken)
    {
        await DeleteIntroductionsToAsync(notification.OdinId);
    }

    public async Task Handle(ConnectionBlockedNotification notification, CancellationToken cancellationToken)
    {
        await using var tx = await _db.BeginStackedTransactionAsync();
        await DeleteIntroductionsToAsync(notification.OdinId);
        await DeleteIntroductionsFromAsync(notification.OdinId);
        tx.Commit();
    }

    public async Task Handle(ConnectionDeletedNotification notification, CancellationToken cancellationToken)
    {
        await using var tx = await _db.BeginStackedTransactionAsync();
        await DeleteIntroductionsToAsync(notification.OdinId);
        await DeleteIntroductionsFromAsync(notification.OdinId);
        tx.Commit();
    }

    public async Task Handle(ConnectionRequestReceivedNotification notification, CancellationToken cancellationToken)
    {
        await AutoAcceptEligibleConnectionRequestAsync(notification.Request, false, notification.OdinContext);
    }

    /// <summary>
    /// Sends connection requests for pending introductions if one has not already been sent or received
    /// </summary>
    private async Task SendIntroductoryConnectionRequestInternalAsync(IdentityIntroduction intro, CancellationToken cancellationToken,
        IOdinContext odinContext)
    {
        var recipient = intro.Identity;
        var introducer = intro.IntroducerOdinId;

        var id = Guid.NewGuid();
        var requestHeader = new ConnectionRequestHeader()
        {
            Id = id,
            Recipient = recipient,
            Message = intro.Message,
            IntroducerOdinId = introducer,
            ContactData = new ContactRequestData(),
            CircleIds = [],
            ConnectionRequestOrigin = ConnectionRequestOrigin.Introduction
        };

        await _circleNetworkRequestService.SendConnectionRequestAsync(requestHeader, cancellationToken, odinContext);
    }

    public async Task SendAutoConnectIntroduceeRequest(IdentityIntroduction iid,
        CancellationToken cancellationToken, IOdinContext odinContext)
    {
        await this.SendIntroductoryConnectionRequestInternalAsync(iid, cancellationToken, odinContext);
    }

    private async Task<IdentityIntroduction> GetIntroductionInternalAsync(OdinId identity)
    {
        var result = await ReceivedIntroductionValueStorage.GetAsync<IdentityIntroduction>(_db.KeyThreeValueCached, identity);
        return result;
    }

    private async Task AutoAcceptAsync(OdinId sender, IOdinContext odinContext)
    {
        var header = new AcceptRequestHeader()
        {
            Sender = sender,
            CircleIds = [],
            ContactData = new ContactRequestData(),
        };

        // [DEBUG-754] Mark introduction-auto-accept as the entry path so it can be told
        // apart from owner-Send-short-circuit and explicit accept-incoming UI calls.
        _logger.LogInformation(
            "[DEBUG-754] Introduction AutoAcceptAsync entry — about to call AcceptConnectionRequestAsync. sender={sender}",
            sender);
        var newContext = OdinContextUpgrades.UsePermissions(odinContext,
            PermissionKeys.ReadCircleMembership,
            PermissionKeys.ManageFeed);

        await _circleNetworkRequestService.AcceptConnectionRequestAsync(header, tryOverrideAcl: true, newContext);
    }

    private async Task SaveAndEnqueueToConnect(IdentityIntroduction iid, Guid driveId)
    {
        var recipient = iid.Identity;

        try
        {
            await ReceivedIntroductionValueStorage.UpsertAsync(_db.KeyThreeValueCached,
                recipient,
                dataTypeKey: iid.IntroducerOdinId.ToHashId().ToByteArray(),
                ReceivedIntroductionDataType, iid);

            if (!_tenantContext.Settings.DisableAutoAcceptIntroductionsForTests)
            {
                var item = new OutboxFileItem
                {
                    Recipient = recipient,
                    Priority = 55, //super high priority to ensure these are sent quickly,
                    Type = OutboxItemType.ConnectIntroducee,
                    AttemptCount = 0,
                    File = new InternalDriveFileId()
                    {
                        DriveId = driveId,
                        FileId = recipient.ToHashId()
                    },
                    DependencyFileId = default,
                    State = new OutboxItemState
                    {
                        TransferInstructionSet = null,
                        OriginalTransitOptions = null,
                        EncryptedClientAuthToken = default,
                        Data = OdinSystemSerializer.Serialize(iid).ToUtf8ByteArray()
                    },
                };

                await _peerOutbox.AddItemAsync(item, useUpsert: true);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to enqueue ConnectIntroducee for recipient: [{recipient}]", recipient);
        }
    }

    private async Task DeleteIntroductionsToAsync(OdinId identity)
    {
        _logger.LogDebug("Deleting introduction sent to {identity}", identity);
        await ReceivedIntroductionValueStorage.DeleteAsync(_db.KeyThreeValueCached, identity);
    }

    private async Task DeleteIntroductionsFromAsync(OdinId introducer)
    {
        _logger.LogDebug("Deleting introduction sent from {identity}", introducer);

        var introductionsFromIdentity =
            await ReceivedIntroductionValueStorage.GetByDataTypeAsync<IdentityIntroduction>(_db.KeyThreeValueCached,
                introducer.ToHashId().ToByteArray());

        foreach (var introduction in introductionsFromIdentity)
        {
            await ReceivedIntroductionValueStorage.DeleteAsync(_db.KeyThreeValueCached, introduction.Identity);
        }
    }

    public async Task DeleteIntroductionsAsync(IOdinContext odinContext, UnixTimeUtc? maxDate = null)
    {
        if (maxDate == null)
        {
            _logger.LogDebug("Deleting all introductions");
        }
        else
        {
            _logger.LogDebug("Deleting all introductions before {maxDate}", maxDate.GetValueOrDefault().ToDateTime().ToShortDateString());
        }

        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendIntroductions);
        var results = await ReceivedIntroductionValueStorage.GetByCategoryAsync<IdentityIntroduction>(_db.KeyThreeValueCached,
            ReceivedIntroductionDataType);
        foreach (var intro in results)
        {
            if (maxDate != null && intro.Received < maxDate)
            {
                await ReceivedIntroductionValueStorage.DeleteAsync(_db.KeyThreeValueCached, intro.Identity);
            }
            else
            {
                await ReceivedIntroductionValueStorage.DeleteAsync(_db.KeyThreeValueCached, intro.Identity);
            }
        }
    }
}