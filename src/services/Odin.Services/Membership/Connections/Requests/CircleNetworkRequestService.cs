using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Fluff;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Contacts;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Optimization.Cdn;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Connections.Verification;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Util;
using Refit;


namespace Odin.Services.Membership.Connections.Requests
{
    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public class CircleNetworkRequestService(
        CircleNetworkService cns,
        ILogger<CircleNetworkRequestService> logger,
        IOdinHttpClientFactory odinHttpClientFactory,
        IMediator mediator,
        TenantContext tenantContext,
        PublicPrivateKeyService publicPrivateKeyService,
        ExchangeGrantService exchangeGrantService,
        IcrKeyService icrKeyService,
        CircleMembershipService circleMembershipService,
        FollowerService followerService,
        FileSystemResolver fileSystemResolver,
        CircleNetworkVerificationService verificationService,
        OdinConfiguration odinConfiguration,
        TableKeyThreeValueCached tblKeyThreeValue,
        ITenantLevel2Cache<CircleNetworkRequestService> cache,
        TenantConfigService tenantConfigService,
        ContactService contactService,
        ContactEnrichmentService contactEnrichmentService,
        StaticFileContentService staticFileContentService,
        VersionUpgradeScheduler versionUpgradeScheduler,
        PeerOutbox peerOutbox)
        : PeerServiceBase(odinHttpClientFactory, cns, fileSystemResolver, odinConfiguration)
    {
        private static readonly byte[] PendingRequestsDataType = Guid.Parse("e8597025-97b8-4736-8f6c-76ae696acd86").ToByteArray();
        private static readonly byte[] SentRequestsDataType = Guid.Parse("32130ad3-d8aa-445a-a932-162cb4d499b4").ToByteArray();

        private const string PendingContextKey = "11e5788a-8117-489e-9412-f2ab2978b46d";

        private readonly ThreeKeyValueStorage _pendingRequestValueStorage =
            TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(PendingContextKey));

        private const string SentContextKey = "27a49f56-dd00-4383-bf5e-cd94e3ac193b";

        private readonly ThreeKeyValueStorage _sentRequestValueStorage =
            TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(SentContextKey));

        private readonly CircleNetworkService _cns = cns;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory = odinHttpClientFactory;

        private readonly OdinConfiguration _odinConfiguration = odinConfiguration;

        /// <summary>
        /// Gets a pending request by its sender
        /// </summary>
        public async Task<ConnectionRequest> GetPendingRequestAsync(OdinId sender, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
            var header = await _pendingRequestValueStorage.GetAsync<PendingConnectionRequestHeader>(tblKeyThreeValue,
                MakePendingRequestsKey(sender));

            if (null == header)
            {
                return null;
            }

            byte[] payloadBytes;

            // Updated to fall back to ecc encryption until we fully remove RSA
            if (null == header.Payload)
            {
                if (null == header.EccEncryptedPayload)
                {
                    logger.LogDebug($"RSA Payload for incoming/pending request from {sender} was null");
                    return null;
                }

                payloadBytes = await publicPrivateKeyService.EccDecryptPayload(header.EccEncryptedPayload, odinContext);
            }
            else
            {
                logger.LogError("GetPendingRequest from {odinId} is old and uses RSA encryption.  This must be deleted and recent", sender);
                throw new OdinClientException("Request is out of date and must be resent");
                // (isValidPublicKey, payloadBytes) =
                //     await publicPrivateKeyService.RsaDecryptPayloadAsync(PublicPrivateKeyType.OnlineKey, header.Payload, odinContext);
                //
                // if (isValidPublicKey == false)
                // {
                //     throw new OdinClientException("Invalid or expired public key", OdinClientErrorCode.InvalidOrExpiredRsaKey);
                // }
            }

            // To use an online key, we need to store most of the payload encrypted but need to know who it's from
            ConnectionRequest request = OdinSystemSerializer.Deserialize<ConnectionRequest>(payloadBytes.ToStringFromUtf8Bytes());
            request.ReceivedTimestampMilliseconds = header.ReceivedTimestampMilliseconds;
            request.SenderOdinId = header.SenderOdinId;
            return request;
        }

        /// <summary>
        /// Gets a list of requests awaiting approval.
        /// </summary>
        /// <returns></returns>
        public async Task<PagedResult<PendingConnectionRequestHeader>> GetPendingRequestsAsync(PageOptions pageOptions,
            IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
            var results = await _pendingRequestValueStorage.GetByCategoryAsync<PendingConnectionRequestHeader>(tblKeyThreeValue,
                PendingRequestsDataType);
            return new PagedResult<PendingConnectionRequestHeader>(pageOptions, 1, results.Select(p => p.Redacted()).ToList());
        }

        /// <summary>
        /// Get outgoing requests awaiting approval by their recipient
        /// </summary>
        /// <returns></returns>
        public async Task<PagedResult<ConnectionRequest>> GetSentRequestsAsync(PageOptions pageOptions, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
            var results = await _sentRequestValueStorage.GetByCategoryAsync<ConnectionRequest>(tblKeyThreeValue, SentRequestsDataType);
            return new PagedResult<ConnectionRequest>(pageOptions, 1, results.ToList());
        }

        /// <summary>
        /// Sends a <see cref="ConnectionRequest"/> as an invitation.
        /// </summary>
        /// <returns></returns>
        public async Task SendConnectionRequestAsync(ConnectionRequestHeader header, CancellationToken cancellationToken,
            IOdinContext odinContext)
        {
            if (header.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwner)
            {
                odinContext.AssertCanManageConnections();
            }

            var recipient = (OdinId)header.Recipient;

            // [DEBUG-754] Trace entry to SendConnectionRequest so we can correlate with the eventual
            // RemoteServerMissingOutgoingRequest throw at line 754 (CircleNetworkRequestService.cs).
            logger.LogInformation(
                "[DEBUG-754] SendConnectionRequestAsync entry. recipient={recipient} origin={origin}",
                recipient, header.ConnectionRequestOrigin);

            if (recipient == odinContext.Caller.OdinId)
            {
                throw new OdinClientException(
                    "I get it, connecting with yourself is critical..yet you sent a connection request to yourself but you are already you",
                    OdinClientErrorCode.ConnectionRequestToYourself);
            }

            // Check if already connected
            var existingConnection = await _cns.GetIcrAsync((OdinId)header.Recipient, odinContext);
            if (existingConnection.Status == ConnectionStatus.Blocked)
            {
                throw new OdinClientException("You've blocked this connection", OdinClientErrorCode.BlockedConnection);
            }

            // [DEBUG-754] Snapshot local state before any branching: ICR status, sent-request,
            // pending-incoming. These three answer "did we have any reason to short-circuit
            // into accept-mode?" If pending-incoming is non-null here, we will go through
            // AcceptConnectionRequestAsync and can hit line 754. PendingConnectionRequestHeader
            // is encrypted-at-rest so we only log presence + timestamp; origin lives inside
            // the encrypted payload.
            var existingSentForTrace = await GetSentRequestInternalAsync(recipient);
            var existingPendingForTrace = await GetPendingRequestInternalAsync(recipient);
            logger.LogInformation(
                "[DEBUG-754] Pre-branch state. recipient={recipient} icrStatus={icrStatus} icrIsConnected={isConnected} " +
                "hasSentRequest={hasSent} sentOrigin={sentOrigin} hasPendingIncoming={hasPending} pendingReceivedAt={pendingReceivedAt}",
                recipient,
                existingConnection.Status,
                existingConnection.IsConnected(),
                existingSentForTrace != null,
                existingSentForTrace?.ConnectionRequestOrigin,
                existingPendingForTrace != null,
                existingPendingForTrace?.ReceivedTimestampMilliseconds.milliseconds);

            if (existingConnection.IsConnected() && header.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwner)
            {
                var verifyResult = await verificationService.VerifyConnectionAsync(recipient, cancellationToken, odinContext);
                logger.LogInformation(
                    "[DEBUG-754] VerifyConnectionAsync result. recipient={recipient} isValid={isValid} remoteWasConnected={remoteConnected}",
                    recipient, verifyResult.IsValid, verifyResult.RemoteIdentityWasConnected);

                if (verifyResult.IsValid)
                {
                    // connection is good
                    throw new OdinClientException("Cannot send connection request to a valid connection",
                        OdinClientErrorCode.CannotSendConnectionRequestToValidConnection);
                }
            }

            if (header.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwnerApp)
            {
                await HandleConnectionRequestInternalForAppAsync(header, odinContext);
                return;
            }

            if (header.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction)
            {
                await HandleConnectionRequestInternalForIntroductionAsync(header, odinContext);
                return;
            }

            if (header.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwner)
            {
                await HandleConnectionRequestInternalForIdentityOwnerAsync(header, odinContext);
            }
        }

        /// <summary>
        /// Sends a connection request as an app-origin "auto-connect" call. Delegates to the
        /// existing <see cref="SendConnectionRequestAsync"/> flow with origin
        /// <see cref="ConnectionRequestOrigin.IdentityOwnerApp"/>, then inspects local state to
        /// describe what actually happened (connected, pending, blocked, etc.) so the calling app
        /// can respond without a second round trip.
        /// </summary>
        public async Task<ConnectionRequestResult> SendAutoConnectRequestAsync(ConnectionRequestHeader header,
            CancellationToken cancellationToken, IOdinContext odinContext)
        {
            if (header == null || string.IsNullOrWhiteSpace(header.Recipient))
            {
                return new ConnectionRequestResult { Outcome = AutoConnectOutcome.InvalidRequest };
            }

            var recipient = (OdinId)header.Recipient;

            // Pre-flight: the existing IdentityOwnerApp send path does not guard against
            // re-sending to an already-connected recipient (see CircleNetworkRequestService.cs
            // line 175 — the guard there is IdentityOwner-only). Short-circuit here so the
            // caller gets a clear AlreadyConnected outcome instead of a redundant round trip.
            // Local IsConnected() can be stale, so verify with the recipient before short-
            // circuiting. App context has no master key, so use tryRepairMissingHash: false.
            // If verification can't be performed (no ICR key access, etc.) or returns invalid,
            // fall through to the send flow so the request can repair the connection.
            var preIcr = await _cns.GetIcrAsync(recipient, odinContext);
            if (preIcr.Status == ConnectionStatus.Blocked)
            {
                return new ConnectionRequestResult { Outcome = AutoConnectOutcome.Blocked };
            }

            if (preIcr.IsConnected() && await IsConnectionRemotelyValidAsync(recipient, cancellationToken, odinContext))
            {
                return new ConnectionRequestResult { Outcome = AutoConnectOutcome.AlreadyConnected };
            }

            // Force app-origin regardless of what the caller passed; the endpoint is the
            // statement of intent.
            var autoHeader = new ConnectionRequestHeader
            {
                Recipient = header.Recipient,
                ContactData = header.ContactData,
                Message = header.Message,
                CircleIds = header.CircleIds,
                IntroducerOdinId = header.IntroducerOdinId,
                ConnectionRequestOrigin = ConnectionRequestOrigin.IdentityOwnerApp,
            };

            // Snapshot whether we already had a pending incoming request from the recipient so
            // we can tell the AcceptedFromExistingIncoming case apart from Connected afterward.
            var hadIncomingPending = await GetPendingRequestInternalAsync(recipient) != null;

            var failure = await TrySendAndMapOutcomeAsync(autoHeader, cancellationToken, odinContext);
            if (failure != null)
            {
                return failure;
            }

            // Send completed without throwing. Inspect local state to decide what actually happened.
            // Local IsConnected() alone isn't enough — our ICR can be Connected (stale, or just
            // marked so by AcceptConnectionRequestAsync) while the recipient still only has a
            // pending-incoming. Verify with the recipient before reporting success.
            var postIcr = await _cns.GetIcrAsync(recipient, odinContext);
            if (postIcr.IsConnected() && await IsConnectionRemotelyValidAsync(recipient, cancellationToken, odinContext))
            {
                return new ConnectionRequestResult
                {
                    Outcome = hadIncomingPending
                        ? AutoConnectOutcome.AcceptedFromExistingIncoming
                        : AutoConnectOutcome.Connected
                };
            }

            return new ConnectionRequestResult { Outcome = AutoConnectOutcome.PendingManualApproval };
        }

        // App-context wrapper around VerifyConnectionAsync. Uses the read-only repair-skip path
        // since IdentityOwnerApp has no master key, and treats any verification exception as
        // "couldn't verify" -> not valid, so the caller can be pessimistic about success.
        private async Task<bool> IsConnectionRemotelyValidAsync(OdinId recipient, CancellationToken cancellationToken,
            IOdinContext odinContext)
        {
            try
            {
                var verification = await verificationService.VerifyConnectionAsync(recipient, cancellationToken, odinContext,
                    tryRepairMissingHash: false);
                return verification.IsValid;
            }
            catch (OdinIdentityVerificationException)
            {
                return false;
            }
            catch (OdinSecurityException)
            {
                return false;
            }
        }

        /// <summary>
        /// Owner-origin counterpart to <see cref="SendAutoConnectRequestAsync"/>: sends a connection
        /// request and returns a <see cref="ConnectionRequestResult"/> describing the outcome.
        /// Forces <see cref="ConnectionRequestOrigin.IdentityOwner"/> regardless of what the caller
        /// passed — the endpoint is the statement of intent. The IdentityOwner origin also ensures
        /// the existing verification guard in <see cref="SendConnectionRequestAsync"/> (which only
        /// runs for IdentityOwner origin) short-circuits on a valid existing connection. Post-send
        /// state is inspected so the local "accept existing incoming" path resolves to
        /// <see cref="AutoConnectOutcome.AcceptedFromExistingIncoming"/> instead of being
        /// mis-reported as <see cref="AutoConnectOutcome.PendingManualApproval"/>.
        /// </summary>
        public async Task<ConnectionRequestResult> SendConnectionRequestWithOutcomeAsync(
            ConnectionRequestHeader header,
            CancellationToken cancellationToken,
            IOdinContext odinContext)
        {
            if (header == null || string.IsNullOrWhiteSpace(header.Recipient))
            {
                return new ConnectionRequestResult { Outcome = AutoConnectOutcome.InvalidRequest };
            }

            var recipient = (OdinId)header.Recipient;

            // Force owner origin regardless of what the caller passed; the endpoint is the
            // statement of intent. Symmetric with SendAutoConnectRequestAsync forcing IdentityOwnerApp.
            var ownerHeader = new ConnectionRequestHeader
            {
                Recipient = header.Recipient,
                ContactData = header.ContactData,
                Message = header.Message,
                CircleIds = header.CircleIds,
                IntroducerOdinId = header.IntroducerOdinId,
                ConnectionRequestOrigin = ConnectionRequestOrigin.IdentityOwner,
            };

            // Snapshot whether we already had a pending incoming request from the recipient so we
            // can tell the AcceptedFromExistingIncoming case apart from Connected afterward.
            var hadIncomingPending = await GetPendingRequestInternalAsync(recipient) != null;

            var failure = await TrySendAndMapOutcomeAsync(ownerHeader, cancellationToken, odinContext);
            if (failure != null)
            {
                return failure;
            }

            // Send completed without throwing. Inspect local state to decide what actually happened.
            // Local IsConnected() alone isn't enough — our ICR can be Connected (stale, or just
            // marked so by AcceptConnectionRequestAsync) while the recipient still only has a
            // pending-incoming. Verify with the recipient before reporting success. Owner origin
            // has the master key, so the repair path inside VerifyConnectionAsync is safe here.
            var postIcr = await _cns.GetIcrAsync(recipient, odinContext);
            if (postIcr.IsConnected())
            {
                IcrVerificationResult verification;
                try
                {
                    verification = await verificationService.VerifyConnectionAsync(recipient, cancellationToken, odinContext);
                }
                catch (OdinIdentityVerificationException)
                {
                    // Couldn't verify — be pessimistic and report as pending so the caller
                    // doesn't get a false AcceptedFromExistingIncoming/Connected.
                    return new ConnectionRequestResult { Outcome = AutoConnectOutcome.PendingManualApproval };
                }

                if (verification.IsValid)
                {
                    return new ConnectionRequestResult
                    {
                        Outcome = hadIncomingPending
                            ? AutoConnectOutcome.AcceptedFromExistingIncoming
                            : AutoConnectOutcome.Connected
                    };
                }
            }

            return new ConnectionRequestResult { Outcome = AutoConnectOutcome.PendingManualApproval };
        }

        /// <summary>
        /// Sends a connection request and translates any thrown exception into a
        /// <see cref="ConnectionRequestResult"/>. Returns null on success so the caller can build
        /// its own success outcome (e.g., Connected vs PendingManualApproval). Single source of
        /// truth for exception→outcome mapping across the auto-connect and owner-send paths.
        /// </summary>
        private async Task<ConnectionRequestResult> TrySendAndMapOutcomeAsync(
            ConnectionRequestHeader header,
            CancellationToken cancellationToken,
            IOdinContext odinContext)
        {
            try
            {
                await SendConnectionRequestAsync(header, cancellationToken, odinContext);
                return null;
            }
            catch (OdinClientException e) when (e.ErrorCode == OdinClientErrorCode.ConnectionRequestToYourself)
            {
                return new ConnectionRequestResult { Outcome = AutoConnectOutcome.InvalidRequest, Detail = e.Message };
            }
            catch (OdinClientException e) when (e.ErrorCode == OdinClientErrorCode.BlockedConnection)
            {
                return new ConnectionRequestResult { Outcome = AutoConnectOutcome.Blocked };
            }
            catch (OdinClientException e) when (e.ErrorCode == OdinClientErrorCode.ConnectionRequestAlreadySent)
            {
                return new ConnectionRequestResult { Outcome = AutoConnectOutcome.OutgoingRequestAlreadyExists };
            }
            catch (OdinClientException e) when (e.ErrorCode == OdinClientErrorCode.IntroductoryRequestAlreadySent)
            {
                return new ConnectionRequestResult { Outcome = AutoConnectOutcome.DuplicateIntroductoryRequest };
            }
            catch (OdinClientException e) when (e.ErrorCode == OdinClientErrorCode.CannotSendConnectionRequestToValidConnection)
            {
                return new ConnectionRequestResult { Outcome = AutoConnectOutcome.AlreadyConnected };
            }
            catch (OdinClientException e) when (e.ErrorCode == OdinClientErrorCode.PublicKeyEncryptionIsInvalid)
            {
                return new ConnectionRequestResult { Outcome = AutoConnectOutcome.RecipientUnreachable };
            }
            catch (OdinClientException e) when (e.ErrorCode == OdinClientErrorCode.RecipientIdentityNotConfigured)
            {
                return new ConnectionRequestResult
                    { Outcome = AutoConnectOutcome.RecipientIdentityNotConfigured, Detail = e.Message };
            }
            catch (OdinClientException e) when (e.ErrorCode == OdinClientErrorCode.RecipientRequiresUpgrade)
            {
                return new ConnectionRequestResult
                    { Outcome = AutoConnectOutcome.RecipientRequiresUpgrade, Detail = e.Message };
            }
            catch (OdinClientException e) when (e.ErrorCode == OdinClientErrorCode.RemoteServerReturnedForbidden)
            {
                return new ConnectionRequestResult { Outcome = AutoConnectOutcome.RecipientRejected, Detail = e.Message };
            }
            catch (OdinRemoteIdentityException e)
            {
                return new ConnectionRequestResult { Outcome = AutoConnectOutcome.RecipientUnreachable, Detail = e.Message };
            }
            catch (OdinClientException e)
            {
                // Any OdinClientException whose error code wasn't caught above is a validation /
                // input-shape failure (ArgumentError, NoErrorCode, etc.) — route to InvalidRequest
                // so the caller gets a specific outcome instead of a generic Failed.
                return new ConnectionRequestResult { Outcome = AutoConnectOutcome.InvalidRequest, Detail = e.Message };
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Send connection request to {recipient} failed with unexpected error",
                    header.Recipient);
                return new ConnectionRequestResult { Outcome = AutoConnectOutcome.Failed, Detail = e.Message };
            }
        }

        private async Task<PendingConnectionRequestHeader> GetPendingRequestInternalAsync(OdinId sender)
        {
            return await _pendingRequestValueStorage
                .GetAsync<PendingConnectionRequestHeader>(tblKeyThreeValue, MakePendingRequestsKey(sender));
        }

        /// <summary>
        /// Stores a new pending/incoming request that is not yet accepted.
        /// </summary>
        public async Task<ConnectionRequestReceipt> ReceiveConnectionRequestAsync(EccEncryptedPayload payload,
            CancellationToken cancellationToken, IOdinContext odinContext)
        {
            odinContext.Caller.AssertCallerIsAuthenticated();

            // Pre-flight: our server must be past initial setup. Otherwise, the ECC key, ICR
            // lookups, and storage are all unreliable — tell the sender so they can reach the
            // recipient out of band.
            if (!await tenantConfigService.IsIdentityServerConfiguredAsync())
            {
                throw new OdinClientException(
                    "Recipient identity server has not completed initial setup",
                    OdinClientErrorCode.RecipientIdentityNotConfigured);
            }

            try
            {
                //TODO: check robot detection code

                if (!await publicPrivateKeyService.IsValidEccPublicKeyAsync(payload.KeyType, payload.EncryptionPublicKeyCrc32))
                {
                    throw new OdinClientException("Encrypted Payload Public Key does not match recipient",
                        OdinClientErrorCode.PublicKeyEncryptionIsInvalid);
                }

                var sender = odinContext.GetCallerOdinIdOrFail();
                var recipient = tenantContext.HostOdinId;

                var existingConnection = await _cns.GetIcrAsync(sender, odinContext, true);
                if (existingConnection.Status == ConnectionStatus.Blocked)
                {
                    throw new OdinSecurityException("Identity is blocked");
                }

                var outgoingTimestamp = await cache.TryGetAsync<Guid>(CacheKey(sender), cancellationToken);
                if (outgoingTimestamp.HasValue)
                {
                    //who short first?  if mine was sent first
                    if (ByteArrayUtil.muidcmp(outgoingTimestamp, payload.TimestampId) == -1)
                    {
                        throw new OdinClientException("Introductory request already sent",
                            OdinClientErrorCode.IntroductoryRequestAlreadySent);
                    }
                }

                var request = new PendingConnectionRequestHeader()
                {
                    SenderOdinId = odinContext.GetCallerOdinIdOrFail(),
                    ReceivedTimestampMilliseconds = UnixTimeUtc.Now(),
                    EccEncryptedPayload = payload
                };

                await UpsertPendingConnectionRequestAsync(request);

                await mediator.Publish(new ConnectionRequestReceivedNotification()
                {
                    Sender = request.SenderOdinId,
                    Recipient = recipient,
                    OdinContext = odinContext,
                    Request = request,
                }, cancellationToken);
            }
            catch (OdinClientException)
            {
                // Already a mapped error code — let the sender receive it verbatim.
                throw;
            }
            catch (OdinSecurityException)
            {
                // Recipient deliberately refused (blocked) — surface as 403 unchanged.
                throw;
            }
            catch (Exception ex)
            {
                // Unknown failure. If the recipient is behind on version, the underlying cause is
                // likely schema/code drift — tell the sender so they can nudge the recipient to
                // upgrade out of band. RequiresUpgradeAsync itself can fail (config storage); if
                // it does, swallow that secondary failure and rethrow the original.
                bool requiresUpgrade = false;
                try
                {
                    (requiresUpgrade, _, _) = await versionUpgradeScheduler.RequiresUpgradeAsync();
                }
                catch (Exception upgradeCheckEx)
                {
                    logger.LogWarning(upgradeCheckEx,
                        "RequiresUpgradeAsync failed while handling ReceiveConnectionRequestAsync error");
                }

                if (requiresUpgrade)
                {
                    logger.LogWarning(ex,
                        "ReceiveConnectionRequestAsync failed; recipient requires version upgrade");
                    throw new OdinClientException(
                        "Recipient identity server requires a version upgrade",
                        OdinClientErrorCode.RecipientRequiresUpgrade);
                }

                throw;
            }

            // Success: hand the sender our public profile card so they can name our contact in this
            // same round-trip. Best-effort — never fail the receipt over a missing/unreadable card.
            var receipt = new ConnectionRequestReceipt();
            try
            {
                var (_, exists, bytes) = await staticFileContentService.GetStaticFileStreamAsync(
                    StaticFileConstants.PublicProfileCardFileName);
                if (exists && bytes is { Length: > 0 })
                {
                    receipt.RecipientPublicCardJson = bytes.ToStringFromUtf8Bytes();
                }
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Could not read public profile card for the connection-request receipt");
            }

            return receipt;
        }

        /// <summary>
        /// Gets a connection request sent to the specified recipient
        /// </summary>
        /// <returns>Returns the <see cref="ConnectionRequest"/> if one exists, otherwise null</returns>
        public async Task<ConnectionRequest> GetSentRequestAsync(OdinId recipient, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);

            return await this.GetSentRequestInternalAsync(recipient);
        }

        /// <summary>
        /// Deletes the sent request record.  If the recipient accepts the request
        /// after it has been deleted, the connection will not be established.
        /// </summary>
        /// <param name="notifyRemote">
        /// When true, the recipient is notified (best-effort, via the outbox) so it withdraws the matching
        /// pending request from its side. When false (the default), the cancel is one-sided and the recipient
        /// keeps its pending request until it independently reconciles the asymmetry.
        /// </param>
        public async Task DeleteSentRequest(OdinId recipient, IOdinContext odinContext, bool notifyRemote = false)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);

            Guid? withdrawalTimestampId = null;
            if (notifyRemote)
            {
                // Capture the request instance id before we delete the record, so the recipient can confirm the
                // withdrawal targets the request it currently holds (and not a newer re-send).
                var sentRequest = await GetSentRequestInternalAsync(recipient);
                withdrawalTimestampId = sentRequest?.OutgoingRequestTimestampId;
            }

            await DeleteSentRequestInternalAsync(recipient);

            if (notifyRemote && withdrawalTimestampId is { } timestampId && timestampId != Guid.Empty)
            {
                await EnqueueWithdrawConnectionRequestAsync(recipient, timestampId);
            }
        }

        /// <summary>
        /// Handles an inbound notification that the caller has withdrawn a connection request they previously sent
        /// us; we remove the matching pending request.  Invoked from the peer perimeter, where the caller's identity
        /// has already been verified by certificate.
        /// </summary>
        public async Task ReceiveConnectionRequestWithdrawalAsync(ConnectionRequestWithdrawal withdrawal, IOdinContext odinContext)
        {
            odinContext.Caller.AssertCallerIsAuthenticated();
            OdinValidationUtils.AssertNotNull(withdrawal, nameof(withdrawal));

            var sender = odinContext.GetCallerOdinIdOrFail();

            var pending = await _pendingRequestValueStorage.GetAsync<PendingConnectionRequestHeader>(
                tblKeyThreeValue, MakePendingRequestsKey(sender));

            if (pending == null)
            {
                // Nothing to withdraw -- already accepted, already deleted, or never received. No-op.
                return;
            }

            // Only honor the withdrawal if it targets the request instance we currently hold. If the caller
            // cancelled an earlier request and then sent a newer one, a stale withdrawal for the earlier request
            // must not remove the newer pending request.
            if (pending.EccEncryptedPayload?.TimestampId != withdrawal.TimestampId)
            {
                logger.LogDebug("Ignoring stale connection-request withdrawal from {sender}; the pending request " +
                                "we hold is a different instance than the one being withdrawn.", sender);
                return;
            }

            await DeletePendingRequestInternal(sender);
        }

        private async Task DeleteSentRequestInternalAsync(OdinId recipient)
        {
            var newKey = MakeSentRequestsKey(recipient);
            var recordFromNewKeyFormat = await _sentRequestValueStorage.GetAsync<ConnectionRequest>(tblKeyThreeValue, newKey);
            if (null != recordFromNewKeyFormat)
            {
                await _sentRequestValueStorage.DeleteAsync(tblKeyThreeValue, newKey);
                return;
            }

            try
            {
                //old method
                await _sentRequestValueStorage.DeleteAsync(tblKeyThreeValue, recipient.ToHashId());
            }
            catch (Exception e)
            {
                throw new OdinSystemException("old key lookup method failed", e);
            }
        }

        /// <summary>
        /// Queues a best-effort, retried notification telling the recipient that we have cancelled a connection
        /// request we sent them, so they withdraw the matching pending request from their side.
        /// </summary>
        private async Task EnqueueWithdrawConnectionRequestAsync(OdinId recipient, Guid timestampId)
        {
            var withdrawal = new ConnectionRequestWithdrawal { TimestampId = timestampId };

            var item = new OutboxFileItem
            {
                Recipient = recipient,
                Priority = 50, //high priority to ensure the withdrawal is delivered promptly
                Type = OutboxItemType.WithdrawConnectionRequest,
                AttemptCount = 0,
                File = new InternalDriveFileId
                {
                    DriveId = SystemDriveConstants.TransientTempDrive.Alias,
                    FileId = recipient.ToHashId()
                },
                DependencyFileId = default,
                State = new OutboxItemState
                {
                    TransferInstructionSet = null,
                    OriginalTransitOptions = null,
                    // The withdrawal is delivered over the certificate-authenticated peer perimeter (no
                    // connection/CAT exists for a pending request), so there is no auth token to carry.
                    EncryptedClientAuthToken = Array.Empty<byte>(),
                    Data = OdinSystemSerializer.Serialize(withdrawal).ToUtf8ByteArray()
                },
            };

            await peerOutbox.AddItemAsync(item, useUpsert: true);
        }

        /// <summary>
        /// Accepts a connection request.  This will store the public key certificate
        /// of the sender then send the recipients public key certificate to the sender.
        /// </summary>
        public async Task AcceptConnectionRequestAsync(AcceptRequestHeader header, bool tryOverrideAcl, IOdinContext odinContext)
        {
            header.Validate();

            // [DEBUG-754] Trace the entry point so we can tell whether AcceptConnectionRequestAsync
            // was reached via the IdentityOwner Send short-circuit, an explicit accept-incoming
            // call from the UI, the Introduction auto-accept, or the App handler.
            logger.LogInformation(
                "[DEBUG-754] AcceptConnectionRequestAsync entry. sender={sender} tryOverrideAcl={tryOverride} authContext={ac}",
                header.Sender, tryOverrideAcl, odinContext.AuthContext);

            var incomingRequest = await GetPendingRequestAsync((OdinId)header.Sender, odinContext);
            if (null == incomingRequest)
            {
                throw new OdinClientException($"No pending request was found from sender [{header.Sender}]",
                    OdinClientErrorCode.IncomingRequestNotFound);
            }

            // [DEBUG-754] Capture the pending-incoming we'll be acting on so the user can confirm
            // it really existed at call time, even if it's no longer in storage when they look later.
            logger.LogInformation(
                "[DEBUG-754] Acting on pending-incoming. sender={sender} origin={origin} introducer={introducer} receivedAt={receivedAt}",
                incomingRequest.SenderOdinId,
                incomingRequest.ConnectionRequestOrigin,
                incomingRequest.IntroducerOdinId,
                incomingRequest.ReceivedTimestampMilliseconds.milliseconds);

            incomingRequest.Validate();
            var senderOdinId = (OdinId)incomingRequest.SenderOdinId;
            AccessExchangeGrant accessGrant = null;

            //Note: this option is used for auto-accepting connection requests for the Invitation feature
            if (tryOverrideAcl)
            {
                //If I had previously sent a connection request; use the ACLs I already created
                var existingSentRequest = await GetSentRequestInternalAsync(senderOdinId);
                if (null != existingSentRequest && existingSentRequest.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwner)
                {
                    accessGrant = existingSentRequest.PendingAccessExchangeGrant;
                }
            }

            logger.LogInformation($"Accept Connection request called for sender {senderOdinId} to {incomingRequest.Recipient}");
            var remoteClientAccessToken = ClientAccessToken.FromPortableBytes64(incomingRequest.ClientAccessToken64);

            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            // Note: We want to use the same shared secret for the identities so let use the shared secret created
            // by the identity who sent the request
            var (accessRegistration, clientAccessTokenReply) = await exchangeGrantService.CreateClientAccessToken(keyStoreKey,
                ClientTokenType.IdentityConnectionRegistration,
                sharedSecret: remoteClientAccessToken.SharedSecret);

            SensitiveByteArray masterKey = odinContext.Caller.HasMasterKey ? odinContext.Caller.GetMasterKey() : null;
            var circles = header.CircleIds?.ToList() ?? new List<GuidId>();
            accessGrant ??= new AccessExchangeGrant()
            {
                MasterKeyEncryptedPeerKey = odinContext.Caller.HasMasterKey
                    ? new SymmetricKeyEncryptedAes(masterKey, keyStoreKey)
                    : null,
                IsRevoked = false,
                CircleGrants = await circleMembershipService.CreateCircleGrantListWithSystemCircleAsync(
                    keyStoreKey,
                    circles,
                    incomingRequest.ConnectionRequestOrigin,
                    masterKey,
                    odinContext),
                AppGrants = await _cns.CreateAppCircleGrantListWithSystemCircle(keyStoreKey, circles,
                    incomingRequest.ConnectionRequestOrigin, masterKey, odinContext),
                PeerClientKey = accessRegistration
            };

            var verificationHash = _cns.CreateVerificationHash(
                incomingRequest.VerificationRandomCode,
                remoteClientAccessToken.SharedSecret);

            EncryptedClientAccessToken encryptedCat = null;
            (EccEncryptedPayload Token, EccEncryptedPayload KeyStoreKey) eccEncryptedKeys = default;

            if (odinContext.Caller.HasMasterKey)
            {
                //TODO: read ICR key from app?
                encryptedCat = await icrKeyService.EncryptClientAccessTokenUsingIrcKeyAsync(remoteClientAccessToken, odinContext);
            }
            else
            {
                //TODO: should we validate all drives are write-only ?
                var keyType = GetPublicPrivateKeyType(incomingRequest.ConnectionRequestOrigin);
                var eccEncryptedCat = await publicPrivateKeyService.EccEncryptPayload(
                    keyType,
                    remoteClientAccessToken.ToPortableBytes());

                var eccEncryptedKeyStoreKey = await publicPrivateKeyService.EccEncryptPayload(keyType,
                    keyStoreKey.GetKey());

                eccEncryptedKeys = (Token: eccEncryptedCat, KeyStoreKey: eccEncryptedKeyStoreKey);
            }

            await _cns.ConnectAsync(senderOdinId,
                accessGrant,
                keys: (encryptedCat, eccEncryptedKeys),
                incomingRequest.ContactData,
                incomingRequest.ConnectionRequestOrigin,
                incomingRequest.IntroducerOdinId,
                verificationHash,
                odinContext);

            keyStoreKey.Wipe();

            // Now tell the remote to establish the connection

            ConnectionRequestReply acceptedReq = new()
            {
                SenderOdinId = tenantContext.HostOdinId,
                ContactData = header.ContactData,
                ClientAccessTokenReply64 = clientAccessTokenReply.ToPortableBytes64(),
                TempKey = incomingRequest.TempRawKey,
                VerificationHash = verificationHash
            };

            var authenticationToken64 = remoteClientAccessToken.ToAuthenticationToken().ToPortableBytes64();

            ApiResponse<NoResultResponse> httpResponse = null;

            try
            {
                await TryRetry.Create()
                    .WithAttempts(_odinConfiguration.Host.PeerOperationMaxAttempts)
                    .WithDelay(_odinConfiguration.Host.PeerOperationDelayMs)
                    .ExecuteAsync(async () =>
                    {
                        var json = OdinSystemSerializer.Serialize(acceptedReq);
                        var encryptedPayload = SharedSecretEncryptedPayload.Encrypt(json.ToUtf8ByteArray(),
                            remoteClientAccessToken.SharedSecret);
                        var d = new Dictionary<string, string>()
                        {
                            { OdinHeaderNames.EstablishConnectionAuthToken, authenticationToken64 }
                        };
                        var client = await _odinHttpClientFactory.CreateClientAsync<ICircleNetworkRequestHttpClient>(senderOdinId,
                            headers: d);

                        httpResponse = await client.EstablishConnection(encryptedPayload);
                    });
            }
            catch (TryRetryException)
            {
                throw new OdinSystemException("Failed to establish connection request.  Either " +
                                              "response was empty or server returned a failure");
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                // [DEBUG-754] Dump everything we know at the moment of failure: the recipient
                // (=senderOdinId here, since this is the accept-flow), the http code, and our
                // current local state. This is the diagnostic snapshot for the
                // "A connected to B / B has nothing for A" puzzle.
                string remoteBody = null;
                try
                {
                    remoteBody = httpResponse.Error?.Content;
                }
                catch
                {
                    // best-effort
                }

                ConnectionRequest pendingNow = null;
                try
                {
                    pendingNow = await GetPendingRequestAsync(senderOdinId, odinContext);
                }
                catch
                {
                    // best-effort
                }

                var sentNow = await GetSentRequestInternalAsync(senderOdinId);
                var icrNow = await _cns.GetIcrAsync(senderOdinId, odinContext, true);

                logger.LogWarning(
                    "[DEBUG-754] EstablishConnection failed. peer={peer} httpStatus={status} httpReason={reason} " +
                    "remoteBody={body} localPendingPresent={hasPending} localPendingOrigin={pendingOrigin} " +
                    "localPendingIntroducer={pendingIntroducer} localSentPresent={hasSent} localSentOrigin={sentOrigin} " +
                    "localIcrStatus={icrStatus} localIcrIsConnected={icrConnected}",
                    senderOdinId,
                    (int)httpResponse.StatusCode,
                    httpResponse.ReasonPhrase,
                    remoteBody,
                    pendingNow != null,
                    pendingNow?.ConnectionRequestOrigin,
                    pendingNow?.IntroducerOdinId,
                    sentNow != null,
                    sentNow?.ConnectionRequestOrigin,
                    icrNow.Status,
                    icrNow.IsConnected());

                if (httpResponse.StatusCode == HttpStatusCode.Forbidden)
                {
                    // The remote server does not have a corresponding outgoing request for this sender
                    throw new OdinClientException("The remote identity does not have a corresponding outgoing request.",
                        OdinClientErrorCode.RemoteServerMissingOutgoingRequest);
                }

                throw new OdinSystemException("Failed to establish connection request.  Either " +
                                              "response was empty or server returned a failure");
            }

            await this.DeleteSentRequestInternalAsync(senderOdinId);
            await this.DeletePendingRequestInternal(senderOdinId);

            // Materialize a contact for the now-connected sender from the card they sent (best-effort).
            await TryUpsertConnectionContactAsync(senderOdinId, CardFromRequestData(incomingRequest.ContactData),
                odinContext, enrichFromPublicIfNoName: false);

            try
            {
                logger.LogDebug("AcceptConnectionRequest - Running SynchronizeChannelFiles");
                await followerService.SynchronizeChannelFilesAsync(senderOdinId, odinContext, remoteClientAccessToken.SharedSecret);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed while trying to sync channels");
            }

            remoteClientAccessToken.AccessTokenHalfKey.Wipe();
            remoteClientAccessToken.SharedSecret.Wipe();
        }

        /// <summary>
        /// Establishes a connection between two individuals.  This must be called
        /// from a recipient who has accepted a sender's connection request
        /// </summary>
        public async Task EstablishConnection(SharedSecretEncryptedPayload payload, string authenticationToken64, IOdinContext odinContext)
        {
            // Note: This method runs under the Transit Context because it's called by another identity
            // therefore, all operations that require master key or owner access must have already been completed

            //TODO: need to add a blacklist and other checks to see if we want to accept the request from the incoming DI

            var authToken = ClientAuthenticationToken.FromPortableBytes64(authenticationToken64);
            var caller = odinContext.GetCallerOdinIdOrFail();
            var originalRequest = await GetSentRequestInternalAsync(caller);

            // [DEBUG-754] We are the recipient (B in the puzzle scenario). Log whether we
            // actually have a sent-request for the caller (A), and our ICR view of A.
            // If originalRequest is null we are about to throw — which surfaces back to A
            // as 403 and triggers the line-754 throw on A's side.
            ConnectionStatus icrStatusForCaller;
            bool icrConnectedForCaller;
            try
            {
                var callerIcr = await _cns.GetIcrAsync(caller, odinContext, true);
                icrStatusForCaller = callerIcr.Status;
                icrConnectedForCaller = callerIcr.IsConnected();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[DEBUG-754] EstablishConnection: failed to load ICR for caller {caller}", caller);
                icrStatusForCaller = ConnectionStatus.None;
                icrConnectedForCaller = false;
            }

            logger.LogInformation(
                "[DEBUG-754] EstablishConnection (recipient side) entry. caller={caller} hasSentRequest={hasSent} " +
                "sentOrigin={sentOrigin} icrStatus={icrStatus} icrIsConnected={icrConnected}",
                caller,
                originalRequest != null,
                originalRequest?.ConnectionRequestOrigin,
                icrStatusForCaller,
                icrConnectedForCaller);

            //Assert that I previously sent a request to the identity attempting to connected with me
            if (null == originalRequest)
            {
                if (await cache.ContainsAsync(CacheKey(caller)))
                {
                    // I have an outgoing request to the caller while the caller is trying to establish a connection with me
                    // this will always be true due to the fact the record is removed AFTER the request is sent AND the fact the 
                    // establish connection is called as part of the outgoing request.
                }

                // this can also happen if the connection was already approved via auto-accept 
                var existingConnection = await _cns.GetIcrAsync(caller, odinContext, true);
                if (existingConnection.IsConnected() && existingConnection.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction)
                {
                    logger.LogDebug("Ignoring EstablishConnection from {caller}. Already connected via introduction", caller);
                    return;
                }

                throw new OdinSecurityException("The original request no longer exists in Sent Requests");
            }

            var recipient = (OdinId)originalRequest.Recipient;

            var (keyStoreKey, sharedSecret) = originalRequest.PendingAccessExchangeGrant
                .PeerClientKey.DecryptUsingClientAuthenticationToken(authToken);
            var payloadBytes = payload.Decrypt(sharedSecret);

            ConnectionRequestReply reply = OdinSystemSerializer.Deserialize<ConnectionRequestReply>(payloadBytes.ToStringFromUtf8Bytes());

            if (!ByteArrayUtil.EquiByteArrayCompare(originalRequest.VerificationHash, reply.VerificationHash))
            {
                throw new OdinSecurityException("The original request's verification has does not match the reply");
            }

            var remoteClientAccessToken = ClientAccessToken.FromPortableBytes64(reply.ClientAccessTokenReply64);

            EncryptedClientAccessToken encryptedCat = null;
            (EccEncryptedPayload Token, EccEncryptedPayload KeyStoreKey) eccEncryptedKeys = default;

            var tempKey = reply.TempKey.ToSensitiveByteArray();
            SensitiveByteArray rawIcrKey = null;

            // if we never had a key...
            if (originalRequest.TempEncryptedIcrKey == null)
            {
                // we're here because the original request was
                // automatically sent (w/o the owner)
                //TODO: should we validate all drives are write-only ?
                var keyType = GetPublicPrivateKeyType(originalRequest.ConnectionRequestOrigin);

                var eccEncryptedCat = await publicPrivateKeyService.EccEncryptPayload(keyType,
                    remoteClientAccessToken.ToPortableBytes());

                var eccEncryptedKeyStoreKey = await publicPrivateKeyService.EccEncryptPayload(keyType,
                    keyStoreKey.GetKey());

                eccEncryptedKeys = (Token: eccEncryptedCat, KeyStoreKey: eccEncryptedKeyStoreKey);
            }
            else
            {
                rawIcrKey = originalRequest.TempEncryptedIcrKey?.DecryptKeyClone(tempKey);
                encryptedCat = EncryptedClientAccessToken.Encrypt(rawIcrKey, remoteClientAccessToken);
            }

            await _cns.ConnectAsync(reply.SenderOdinId,
                originalRequest.PendingAccessExchangeGrant,
                keys: (encryptedCat, eccEncryptedKeys),
                reply.ContactData,
                originalRequest.ConnectionRequestOrigin,
                originalRequest.IntroducerOdinId,
                originalRequest.VerificationHash,
                odinContext);

            try
            {
                if (originalRequest.TempEncryptedFeedDriveStorageKey != null)
                {
                    var feedDriveId = SystemDriveConstants.FeedDrive.Alias;
                    var patchedContext = OdinContextUpgrades.PrepForSynchronizeChannelFiles(odinContext,
                        feedDriveId,
                        tempKey,
                        originalRequest.TempEncryptedFeedDriveStorageKey,
                        originalRequest.TempEncryptedIcrKey);

                    logger.LogDebug("EstablishConnection - Running SynchronizeChannelFiles");
                    await followerService.SynchronizeChannelFilesAsync(recipient, patchedContext, sharedSecret);
                }
                else
                {
                    logger.LogDebug("skipping Feed drive sync since no temp feed drive storage key was available");
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to sync channel files");
            }

            rawIcrKey?.Wipe();
            tempKey.Wipe();

            await this.DeleteSentRequestInternalAsync(recipient);
            await this.DeletePendingRequestInternal(recipient);

            if (originalRequest.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction)
            {
                await mediator.Publish(new IntroductionsAcceptedNotification()
                {
                    Recipient = recipient,
                    IntroducerOdinId = originalRequest.IntroducerOdinId.GetValueOrDefault(),
                    OdinContext = odinContext,
                });
            }
            else
            {
                await mediator.Publish(new ConnectionRequestAcceptedNotification()
                {
                    Sender = (OdinId)originalRequest.SenderOdinId,
                    Recipient = recipient,
                    OdinContext = odinContext,
                });
            }
        }

        /// <summary>
        /// Deletes a pending request.  This is useful if the user decides to ignore a request.
        /// </summary>
        public Task DeletePendingRequest(OdinId sender, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ManageContacts);
            return DeletePendingRequestInternal(sender);
        }

        public async Task<bool> HasPendingOrSentRequest(OdinId identity, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
            var hasPendingRequest = await HasPendingRequestInternal(identity);
            if (hasPendingRequest)
            {
                return true;
            }

            var hasSentRequest = await GetSentRequestInternalAsync(identity);
            if (null != hasSentRequest)
            {
                return true;
            }

            return false;
        }

        private async Task<bool> HasPendingRequestInternal(OdinId sender)
        {
            var header = await _pendingRequestValueStorage.GetAsync<PendingConnectionRequestHeader>(tblKeyThreeValue,
                MakePendingRequestsKey(sender));
            return header != null;
        }

        private async Task DeletePendingRequestInternal(OdinId sender)
        {
            var newKey = MakePendingRequestsKey(sender);
            var recordFromNewKeyFormat = await _pendingRequestValueStorage
                .GetAsync<PendingConnectionRequestHeader>(tblKeyThreeValue, newKey);

            if (null != recordFromNewKeyFormat)
            {
                await _pendingRequestValueStorage.DeleteAsync(tblKeyThreeValue, newKey);
                return;
            }

            //try the old key
            try
            {
                await _pendingRequestValueStorage.DeleteAsync(tblKeyThreeValue, sender.ToHashId());
            }
            catch (Exception e)
            {
                throw new OdinSystemException("Failed with old key lookup method.", e);
            }
        }

        private async Task UpsertSentConnectionRequestAsync(ConnectionRequest request)
        {
            request.SenderOdinId = tenantContext.HostOdinId; //store for when we support multiple domains per identity
            await _sentRequestValueStorage.UpsertAsync(tblKeyThreeValue, MakeSentRequestsKey(new OdinId(request.Recipient)), GuidId.Empty,
                SentRequestsDataType,
                request);
        }

        private async Task UpsertPendingConnectionRequestAsync(PendingConnectionRequestHeader request)
        {
            await _pendingRequestValueStorage.UpsertAsync(tblKeyThreeValue, MakePendingRequestsKey(request.SenderOdinId), GuidId.Empty,
                PendingRequestsDataType,
                request);
        }

        private async Task<ConnectionRequest> GetSentRequestInternalAsync(OdinId recipient)
        {
            var result = await _sentRequestValueStorage.GetAsync<ConnectionRequest>(tblKeyThreeValue, MakeSentRequestsKey(recipient));
            return result;
        }

        private Guid MakeSentRequestsKey(OdinId recipient)
        {
            var combined = ByteArrayUtil.Combine(recipient.ToHashId().ToByteArray(), SentRequestsDataType);
            var bytes = ByteArrayUtil.ReduceSHA256Hash(combined);
            return new Guid(bytes);
        }

        private Guid MakePendingRequestsKey(OdinId sender)
        {
            var combined = ByteArrayUtil.Combine(sender.ToHashId().ToByteArray(), PendingRequestsDataType);
            var bytes = ByteArrayUtil.ReduceSHA256Hash(combined);
            return new Guid(bytes);
        }

        /// <summary>
        /// Best-effort: upsert a contact for <paramref name="odinId"/> from the connection-flow contact
        /// data (the peer's full <see cref="ContactContent"/> card when present, else their name) and,
        /// when <paramref name="enrichFromProfile"/> is set, fill it from the peer's profile — on send
        /// that's their <b>public</b> profile (keyless), so the contact always gets a real name rather
        /// than an empty record. Skips silently when the context lacks the ContactDrive storage key
        /// (peer/introduction) and never throws into the connection flow.
        /// </summary>
        private async Task TryUpsertConnectionContactAsync(OdinId odinId, PeerContactContent card,
            IOdinContext odinContext, bool enrichFromPublicIfNoName)
        {
            try
            {
                if (!odinContext.PermissionsContext.TryGetDriveStorageKey(SystemDriveConstants.ContactDrive.Alias, out _))
                {
                    // No ContactDrive storage key here (peer/introduction context). The peer's card is
                    // preserved on the ICR; an owner-keyed /sync can materialize the contact later.
                    return;
                }

                var content = card ?? new PeerContactContent();
                content.OdinId = odinId.DomainName;   // authoritative identity for the contact
                content.Source = "contact";           // connection-derived

                await contactService.MergeAsync(content, ContactMergeSource.Api, odinContext);

                if (enrichFromPublicIfNoName && string.IsNullOrWhiteSpace(content.Name?.DisplayName))
                {
                    // No name from the delivery receipt — pull the peer's public profile (keyless).
                    await contactEnrichmentService.EnrichAsync(odinId, odinContext);
                }
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Connection-flow contact upsert for {odinId} failed; ignoring", odinId);
            }
        }

        /// <summary>Builds a contact card from the peer's request data — their full shared card, else their name.</summary>
        private static PeerContactContent CardFromRequestData(ContactRequestData data)
        {
            if (data?.Contact != null)
            {
                return data.Contact;
            }

            return string.IsNullOrWhiteSpace(data?.Name)
                ? null
                : new PeerContactContent { Name = new ContactName { DisplayName = data.Name } };
        }

        /// <summary>Builds a contact card from the recipient's public profile card returned on delivery.</summary>
        private static PeerContactContent CardFromReceipt(ConnectionRequestReceipt receipt)
        {
            if (string.IsNullOrWhiteSpace(receipt?.RecipientPublicCardJson))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(receipt.RecipientPublicCardJson);
                if (doc.RootElement.TryGetProperty("name", out var name) &&
                    name.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(name.GetString()))
                {
                    return new PeerContactContent { Name = new ContactName { DisplayName = name.GetString() } };
                }
            }
            catch
            {
                // malformed card — fall back to the public-profile fetch
            }

            return null;
        }

        private async Task HandleConnectionRequestInternalForIdentityOwnerAsync(ConnectionRequestHeader header, IOdinContext odinContext)
        {
            odinContext.AssertCanManageConnections();
            var masterKey = odinContext.Caller.GetMasterKey();

            var recipient = (OdinId)header.Recipient;

            logger.LogDebug("Sending Identity-owner-connection request to {recipient}", recipient);

            var incomingRequest = await this.GetPendingRequestAsync(recipient, odinContext);
            if (incomingRequest != null)
            {
                // [DEBUG-754] We are short-circuiting Send into Accept because a pending-incoming
                // from the recipient was found. This is the path that leads to line 754 if the
                // remote 403s on EstablishConnection.
                logger.LogInformation(
                    "[DEBUG-754] IdentityOwner short-circuit: pending-incoming present for {recipient}; " +
                    "diverting Send into AcceptConnectionRequestAsync. " +
                    "incomingSender={sender} incomingOrigin={origin} incomingIntroducer={introducer}",
                    recipient,
                    incomingRequest.SenderOdinId,
                    incomingRequest.ConnectionRequestOrigin,
                    incomingRequest.IntroducerOdinId);

                // just accept the request; using the ACL info (header.CircleIds, etc.)
                var ac = new AcceptRequestHeader
                {
                    Sender = recipient,
                    CircleIds = header.CircleIds,
                    ContactData = header.ContactData
                };

                await this.AcceptConnectionRequestAsync(ac, tryOverrideAcl: false, odinContext);
                return;
            }

            var existingOutgoingRequest = await this.GetSentRequestInternalAsync(recipient);
            if (null == existingOutgoingRequest)
            {
                logger.LogInformation(
                    "[DEBUG-754] IdentityOwner: no pending-incoming, no existing sent-request — creating fresh outgoing to {recipient}",
                    recipient);
                await CreateAndSendRequestInternalAsync(header, masterKey, odinContext);
            }
            else
            {
                logger.LogInformation(
                    "[DEBUG-754] IdentityOwner: no pending-incoming; existing sent-request present (origin={origin}) for {recipient}",
                    existingOutgoingRequest.ConnectionRequestOrigin, recipient);

                if (existingOutgoingRequest.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction)
                {
                    //overwrite this with new request and send it
                    await CreateAndSendRequestInternalAsync(header, masterKey, odinContext);
                }
                else if (existingOutgoingRequest.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwner)
                {
                    //merge with existing request
                    var newCircles = header.CircleIds ?? [];
                    var exitingCircles = existingOutgoingRequest.PendingAccessExchangeGrant.CircleGrants.Keys.Select(c => new GuidId(c))
                        .ToList();
                    newCircles.AddRange(exitingCircles.Where(c => !newCircles.Exists(nc => nc == c)).ToList());
                    header.CircleIds = newCircles;
                    await CreateAndSendRequestInternalAsync(header, masterKey, odinContext);
                }
            }
        }

        private async Task HandleConnectionRequestInternalForIntroductionAsync(ConnectionRequestHeader header, IOdinContext odinContext)
        {
            OdinValidationUtils.AssertNotNullOrEmpty(header.IntroducerOdinId, nameof(header.IntroducerOdinId));

            //
            // validate you can only have write access to drives with allowAnonymous = false
            //

            // TODO - ?? not yet sure how I want to handle this
            // await ValidateWriteOnlyDriveGrants(header, odinContext, cn);

            var recipient = (OdinId)header.Recipient;

            logger.LogDebug("Sending Introduced-connection request to {recipient}", recipient);

            if (tenantContext.Settings.DisableAutoAcceptIntroductionsForTests)
            {
                var existingOutgoingRequest = await this.GetSentRequestInternalAsync(recipient);
                if (null == existingOutgoingRequest)
                {
                    await CreateAndSendRequestInternalAsync(header, masterKey: null, odinContext);
                }
                else
                {
                    var existingRequestOrigin = existingOutgoingRequest.ConnectionRequestOrigin;
                    if (existingRequestOrigin == ConnectionRequestOrigin.Introduction)
                    {
                        // overwrite this with newly incoming request and send it
                        // reason: the new request is created by the owner, so it will more explicit info (like circles)
                        await CreateAndSendRequestInternalAsync(header, masterKey: null, odinContext);
                    }
                    else if (existingRequestOrigin == ConnectionRequestOrigin.IdentityOwner)
                    {
                        // Resend the request - using the circles from the existing request
                        header.CircleIds = existingOutgoingRequest.PendingAccessExchangeGrant.CircleGrants.Keys.Select(c => new GuidId(c))
                            .ToList();
                        await CreateAndSendRequestInternalAsync(header, masterKey: null, odinContext);
                    }
                }
            }
            else
            {
                var incomingRequest = await this.GetPendingRequestAsync(recipient, odinContext);
                if (incomingRequest == null)
                {
                    await CreateAndSendRequestInternalAsync(header, masterKey: null, odinContext);
                }
                else
                {
                    // just accept the request; using the ACL info (header.CircleIds, etc.)
                    var ac = new AcceptRequestHeader
                    {
                        Sender = recipient,
                        CircleIds = header.CircleIds,
                        ContactData = header.ContactData
                    };

                    await this.AcceptConnectionRequestAsync(ac, tryOverrideAcl: false, odinContext);
                }
            }
        }

        private async Task HandleConnectionRequestInternalForAppAsync(ConnectionRequestHeader header, IOdinContext ctx)
        {
            ctx.Caller.AssertCallerIsOwner();
            var odinContext = OdinContextUpgrades.UsePermissions(ctx, PermissionKeys.ReadCircleMembership);

            var recipient = (OdinId)header.Recipient;

            logger.LogDebug("Sending app-connection request to {recipient}; meaning there is no master key", recipient);

            var incomingRequest = await this.GetPendingRequestAsync(recipient, odinContext);
            if (incomingRequest != null)
            {
                // just accept the request; using the ACL info (header.CircleIds, etc.)
                var ac = new AcceptRequestHeader
                {
                    Sender = recipient,
                    CircleIds = header.CircleIds,
                    ContactData = header.ContactData
                };

                await this.AcceptConnectionRequestAsync(ac, tryOverrideAcl: false, odinContext);
                return;
            }

            var existingOutgoingRequest = await this.GetSentRequestInternalAsync(recipient);
            if (null == existingOutgoingRequest)
            {
                await CreateAndSendRequestInternalAsync(header, masterKey: null, odinContext);
                return;
            }

            var existingRequestOrigin = existingOutgoingRequest.ConnectionRequestOrigin;
            if (existingRequestOrigin == ConnectionRequestOrigin.Introduction)
            {
                // overwrite with the new app-initiated request and resend
                await CreateAndSendRequestInternalAsync(header, masterKey: null, odinContext);
            }
            else if (existingRequestOrigin == ConnectionRequestOrigin.IdentityOwnerApp)
            {
                // Resend in case something changed on the recipient side (e.g. they
                // deleted the pending request). Merge circles so we don't lose any
                // grants from the earlier app-origin request.
                var newCircles = header.CircleIds ?? [];
                var existingCircles = existingOutgoingRequest.PendingAccessExchangeGrant.CircleGrants.Keys
                    .Select(c => new GuidId(c))
                    .ToList();
                newCircles.AddRange(existingCircles.Where(c => !newCircles.Exists(nc => nc == c)).ToList());
                header.CircleIds = newCircles;
                await CreateAndSendRequestInternalAsync(header, masterKey: null, odinContext);
            }
            else if (existingRequestOrigin == ConnectionRequestOrigin.IdentityOwner)
            {
                // An owner-console request already exists; the app path has no master
                // key so it cannot reproduce the ICR-key-bound grant — refuse rather
                // than silently downgrade.
                throw new OdinClientException("There is an existing outgoing connection request",
                    OdinClientErrorCode.ConnectionRequestAlreadySent);
            }
        }

        private async Task CreateAndSendRequestInternalAsync(ConnectionRequestHeader header, SensitiveByteArray masterKey,
            IOdinContext odinContext)
        {
            var recipient = (OdinId)header.Recipient;

            //TODO: scalability - _outgoingIntroductionRequests needs to work across servers
            var timestamp = SequentialGuid.CreateGuid();
            await cache.SetAsync(CacheKey(recipient), timestamp, TimeSpan.FromHours(1));

            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var circles = header.CircleIds?.ToList() ?? new List<GuidId>();
            var (clientAccessToken, grant) = await CreateTokenAndExchangeGrantAsync(keyStoreKey, circles, header.ConnectionRequestOrigin,
                masterKey, odinContext);

            var tempRawKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var randomCode = ByteArrayUtil.GetRandomCryptoGuid();
            var outgoingRequest = new ConnectionRequest
            {
                Id = header.Id,
                ContactData = header.ContactData,
                Recipient = header.Recipient,
                Message = header.Message,
                ClientAccessToken64 = "",
                TempRawKey = null,
                OutgoingRequestTimestampId = timestamp,
                VerificationRandomCode = randomCode,
                ConnectionRequestOrigin = header.ConnectionRequestOrigin,
                IntroducerOdinId = header.IntroducerOdinId,
                PendingAccessExchangeGrant = grant,
                VerificationHash = _cns.CreateVerificationHash(randomCode, clientAccessToken.SharedSecret)
            };

            if (header.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwner)
            {
                var rawIcrKey = odinContext.PermissionsContext.GetIcrKey();
                outgoingRequest.TempEncryptedIcrKey = new SymmetricKeyEncryptedAes(tempRawKey, rawIcrKey);

                var feedDriveStorageKey = odinContext.PermissionsContext.GetDriveStorageKey(SystemDriveConstants.FeedDrive.Alias);
                outgoingRequest.TempEncryptedFeedDriveStorageKey = new SymmetricKeyEncryptedAes(tempRawKey, feedDriveStorageKey);
            }

            try
            {
                await UpsertSentConnectionRequestAsync(outgoingRequest);

                // Clean up items we do not want being sent to the recipient
                //give the key to the recipient, so they can give it back when they accept the request
                outgoingRequest.TempRawKey = tempRawKey.GetKey();
                outgoingRequest.ClientAccessToken64 = clientAccessToken.ToPortableBytes64();
                outgoingRequest.VerificationHash = null;
                outgoingRequest.PendingAccessExchangeGrant = null;
                outgoingRequest.TempEncryptedIcrKey = null;
                outgoingRequest.TempEncryptedFeedDriveStorageKey = null;
                clientAccessToken.SharedSecret.Wipe();
                clientAccessToken.AccessTokenHalfKey.Wipe();

                var (sendSucceeded, receipt) = await TrySendRequestInternalAsync((OdinId)header.Recipient, outgoingRequest, timestamp);
                if (!sendSucceeded)
                {
                    await DeleteSentRequestInternalAsync(recipient);
                }
                else
                {
                    // Name the recipient's contact from the public card they returned on delivery; fall
                    // back to a keyless pub/profile fetch if the card wasn't provided. Best-effort.
                    await TryUpsertConnectionContactAsync(recipient, CardFromReceipt(receipt), odinContext,
                        enrichFromPublicIfNoName: true);
                }
            }
            finally
            {
                await cache.RemoveAsync(CacheKey(recipient));
            }

            keyStoreKey.Wipe();
            tempRawKey.Wipe();
        }

        private async Task<(ClientAccessToken clientAccessToken, AccessExchangeGrant)> CreateTokenAndExchangeGrantAsync(
            SensitiveByteArray keyStoreKey,
            List<GuidId> circles,
            ConnectionRequestOrigin origin,
            SensitiveByteArray masterKey,
            IOdinContext odinContext)
        {
            var (accessRegistration, clientAccessToken) = await exchangeGrantService.CreateClientAccessToken(
                keyStoreKey,
                ClientTokenType.IdentityConnectionRegistration);

            var grant = new AccessExchangeGrant()
            {
                // We allow this to be null in the case of connection requests coming due to an introduction
                MasterKeyEncryptedPeerKey = masterKey == null ? null : new SymmetricKeyEncryptedAes(masterKey, keyStoreKey),
                IsRevoked = false,
                CircleGrants = await circleMembershipService.CreateCircleGrantListWithSystemCircleAsync(
                    keyStoreKey,
                    circles,
                    origin,
                    masterKey,
                    odinContext),
                AppGrants = await _cns.CreateAppCircleGrantListWithSystemCircle(keyStoreKey, circles, origin, masterKey, odinContext),
                PeerClientKey = accessRegistration
            };

            return (clientAccessToken, grant);
        }

        private async Task<(bool success, ConnectionRequestReceipt receipt)> TrySendRequestInternalAsync(
            OdinId recipient, ConnectionRequest request, Guid timestamp)
        {
            var keyType = GetPublicPrivateKeyType(request.ConnectionRequestOrigin);

            async Task<(bool encryptionSucceeded, ApiResponse<ConnectionRequestReceipt> deliveryResponse)> Send()
            {
                EccEncryptedPayload eccEncryptedPayload;
                try
                {
                    var payloadBytes = OdinSystemSerializer.Serialize(request).ToUtf8ByteArray();
                    eccEncryptedPayload = await publicPrivateKeyService.EccEncryptPayloadForRecipientAsync(
                        keyType,
                        recipient,
                        payloadBytes);
                }
                catch (OdinRemoteIdentityException e)
                {
                    logger.LogInformation(e, "Failed to encrypt payload for recipient");
                    return (false, null);
                }

                var client = await _odinHttpClientFactory.CreateClientAsync<ICircleNetworkRequestHttpClient>(recipient);

                eccEncryptedPayload.TimestampId = timestamp;

                var response = await client.DeliverConnectionRequest(eccEncryptedPayload);

                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var code = response.Error.ParseProblemDetails();
                    if (code == OdinClientErrorCode.PublicKeyEncryptionIsInvalid)
                    {
                        return (false, response);
                    }
                }

                return (true, response);
            }

            var sendResult1 = await Send();
            if (sendResult1.encryptionSucceeded)
            {
                if (sendResult1.deliveryResponse.StatusCode == HttpStatusCode.BadRequest)
                {
                    var code = sendResult1.deliveryResponse.Error.ParseProblemDetails();
                    if (code == OdinClientErrorCode.IntroductoryRequestAlreadySent)
                    {
                        return (false, null);
                        // there was already a request sent, bubble this up
                        // throw new OdinClientException("Remote server already sent a request",
                        //     OdinClientErrorCode.IntroductoryRequestAlreadySent);
                    }

                    if (code == OdinClientErrorCode.RecipientIdentityNotConfigured)
                    {
                        throw new OdinClientException(
                            "Recipient identity server has not completed initial setup",
                            OdinClientErrorCode.RecipientIdentityNotConfigured);
                    }

                    if (code == OdinClientErrorCode.RecipientRequiresUpgrade)
                    {
                        throw new OdinClientException(
                            "Recipient identity server requires a version upgrade",
                            OdinClientErrorCode.RecipientRequiresUpgrade);
                    }
                }
            }
            else
            {
                logger.LogDebug("TrySendRequestInternal to {recipient} failed the first time. " +
                                "Invalidating public key cache and retrying", recipient);

                await publicPrivateKeyService.InvalidateRecipientEccPublicKeyAsync(keyType, recipient);
                sendResult1 = await Send();

                if (!sendResult1.encryptionSucceeded)
                {
                    throw new OdinRemoteIdentityException("Failed to encrypt payload for recipient");
                }
            }

            if (sendResult1.deliveryResponse.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new OdinClientException("Remote server denied connection",
                    OdinClientErrorCode.RemoteServerReturnedForbidden);
            }

            if (!sendResult1.deliveryResponse.IsSuccessStatusCode)
            {
                var sendResult2 = await Send();
                if (sendResult2.deliveryResponse.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new OdinClientException("Remote server denied connection",
                        OdinClientErrorCode.RemoteServerReturnedForbidden);
                }

                if (!sendResult2.deliveryResponse.IsSuccessStatusCode)
                {
                    throw new OdinClientException("Failed to establish connection request");
                }

                return (true, sendResult2.deliveryResponse.Content);
            }

            return (true, sendResult1.deliveryResponse.Content);
        }

        private PublicPrivateKeyType GetPublicPrivateKeyType(ConnectionRequestOrigin origin)
        {
            return origin == ConnectionRequestOrigin.IdentityOwner
                ? PublicPrivateKeyType.OnlineIcrEncryptedKey
                : PublicPrivateKeyType.OfflineKey;
        }

        private static string CacheKey(Guid uuid)
        {
            return "OutgoingIntroductionRequests:" + uuid;
        }
    }
}