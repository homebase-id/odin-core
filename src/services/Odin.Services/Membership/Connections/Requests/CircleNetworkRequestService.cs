using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Connections.Verification;
using Odin.Services.Peer;
using Odin.Services.Util;
using Refit;


namespace Odin.Services.Membership.Connections.Requests
{
    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public class CircleNetworkRequestService : PeerServiceBase
    {
        private readonly byte[] _pendingRequestsDataType = Guid.Parse("e8597025-97b8-4736-8f6c-76ae696acd86").ToByteArray();

        private readonly byte[] _sentRequestsDataType = Guid.Parse("32130ad3-d8aa-445a-a932-162cb4d499b4").ToByteArray();


        private readonly CircleNetworkService _cns;
        private readonly ILogger<CircleNetworkRequestService> _logger;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;

        private readonly IMediator _mediator;
        private readonly TenantContext _tenantContext;
        private readonly PublicPrivateKeyService _publicPrivateKeyService;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly IcrKeyService _icrKeyService;
        private readonly DriveManager _driveManager;
        private readonly CircleMembershipService _circleMembershipService;
        private readonly FollowerService _followerService;
        private readonly CircleNetworkVerificationService _verificationService;
        private readonly OdinConfiguration _odinConfiguration;
        private readonly ThreeKeyValueStorage _pendingRequestValueStorage;
        private readonly ThreeKeyValueStorage _sentRequestValueStorage;

        public CircleNetworkRequestService(
            CircleNetworkService cns,
            ILogger<CircleNetworkRequestService> logger,
            IOdinHttpClientFactory odinHttpClientFactory,
            TenantSystemStorage tenantSystemStorage,
            IMediator mediator,
            TenantContext tenantContext,
            PublicPrivateKeyService publicPrivateKeyService,
            ExchangeGrantService exchangeGrantService,
            IcrKeyService icrKeyService,
            CircleMembershipService circleMembershipService,
            DriveManager driveManager,
            FollowerService followerService,
            FileSystemResolver fileSystemResolver,
            CircleNetworkVerificationService verificationService,
            OdinConfiguration odinConfiguration)
            : base(odinHttpClientFactory, cns, fileSystemResolver)
        {
            _cns = cns;
            _logger = logger;
            _odinHttpClientFactory = odinHttpClientFactory;
            _mediator = mediator;
            _tenantContext = tenantContext;
            _publicPrivateKeyService = publicPrivateKeyService;
            _exchangeGrantService = exchangeGrantService;
            _icrKeyService = icrKeyService;
            _circleMembershipService = circleMembershipService;
            _driveManager = driveManager;
            _followerService = followerService;
            _verificationService = verificationService;
            _odinConfiguration = odinConfiguration;


            const string pendingContextKey = "11e5788a-8117-489e-9412-f2ab2978b46d";
            _pendingRequestValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(pendingContextKey));

            const string sentContextKey = "27a49f56-dd00-4383-bf5e-cd94e3ac193b";
            _sentRequestValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(sentContextKey));
        }

        /// <summary>
        /// Gets a pending request by its sender
        /// </summary>
        public async Task<ConnectionRequest> GetPendingRequest(OdinId sender, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.AssertCanManageConnections();
            var header = _pendingRequestValueStorage.Get<PendingConnectionRequestHeader>(cn, MakePendingRequestsKey(sender));

            if (null == header)
            {
                return null;
            }

            if (null == header.Payload)
            {
                _logger.LogWarning($"RSA Payload for incoming/pending request from {sender} was null");
                return null;
            }

            var (isValidPublicKey, payloadBytes) =
                await _publicPrivateKeyService.RsaDecryptPayload(PublicPrivateKeyType.OnlineKey, header.Payload, odinContext, cn);
            if (isValidPublicKey == false)
            {
                throw new OdinClientException("Invalid or expired public key", OdinClientErrorCode.InvalidOrExpiredRsaKey);
            }

            // To use an online key, we need to store most of the payload encrypted but need to know who it's from
            ConnectionRequest request = OdinSystemSerializer.Deserialize<ConnectionRequest>(payloadBytes.ToStringFromUtf8Bytes());
            request.ReceivedTimestampMilliseconds = header.ReceivedTimestampMilliseconds;
            request.SenderOdinId = header.SenderOdinId;
            return await Task.FromResult(request);
        }

        /// <summary>
        /// Gets a list of requests awaiting approval.
        /// </summary>
        /// <returns></returns>
        public async Task<PagedResult<PendingConnectionRequestHeader>> GetPendingRequests(PageOptions pageOptions, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
            var results = _pendingRequestValueStorage.GetByCategory<PendingConnectionRequestHeader>(cn, _pendingRequestsDataType);
            return await Task.FromResult(new PagedResult<PendingConnectionRequestHeader>(pageOptions, 1, results.Select(p => p.Redacted()).ToList()));
        }

        /// <summary>
        /// Get outgoing requests awaiting approval by their recipient
        /// </summary>
        /// <returns></returns>
        public async Task<PagedResult<ConnectionRequest>> GetSentRequests(PageOptions pageOptions, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
            var results = _sentRequestValueStorage.GetByCategory<ConnectionRequest>(cn, _sentRequestsDataType);
            return await Task.FromResult(new PagedResult<ConnectionRequest>(pageOptions, 1, results.ToList()));
        }

        /// <summary>
        /// Sends a <see cref="ConnectionRequest"/> as an invitation.
        /// </summary>
        /// <returns></returns>
        public async Task SendConnectionRequest(ConnectionRequestHeader header, IOdinContext odinContext, DatabaseConnection cn)
        {
            var recipient = (OdinId)header.Recipient;
            if (recipient == odinContext.Caller.OdinId)
            {
                throw new OdinClientException(
                    "I get it, connecting with yourself is critical..yet you sent a connection request to yourself but you are already you",
                    OdinClientErrorCode.ConnectionRequestToYourself);
            }

            // Check if already connected
            var existingConnection = await _cns.GetIcr((OdinId)header.Recipient, odinContext, cn);
            if (existingConnection.Status == ConnectionStatus.Blocked)
            {
                throw new OdinClientException("You've blocked this connection", OdinClientErrorCode.BlockedConnection);
            }

            if (existingConnection.IsConnected())
            {
                if ((await _verificationService.VerifyConnection(recipient, odinContext, cn)).IsValid)
                {
                    // connection is good
                    throw new OdinClientException("Cannot send connection request to a valid connection",
                        OdinClientErrorCode.CannotSendConnectionRequestToValidConnection);
                }
            }

            if (header.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction)
            {
                await HandleConnectionRequestInternalForIntroduction(header, odinContext, cn);
                return;
            }

            if (header.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwner)
            {
                await HandleConnectionRequestInternalForIdentityOwner(header, odinContext, cn);
            }
        }

        /// <summary>
        /// Stores an new pending/incoming request that is not yet accepted.
        /// </summary>
        public async Task ReceiveConnectionRequest(RsaEncryptedPayload payload, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertCallerIsAuthenticated();

            //TODO: check robot detection code

            var sender = odinContext.GetCallerOdinIdOrFail();
            var recipient = _tenantContext.HostOdinId;

            var existingConnection = await _cns.GetIcr(sender, odinContext, cn, true);
            if (existingConnection.Status == ConnectionStatus.Blocked)
            {
                // throw new OdinClientException("Blocked", OdinClientErrorCode.BlockedConnection);
                throw new OdinSecurityException("Identity is blocked");
            }

            //TODO: I removed this because the caller does not have the required shared secret; will revisit later if checking this is crucial
            if (existingConnection.IsConnected())
            {
                try
                {
                    if ((await _verificationService.VerifyConnection(sender, odinContext, cn)).IsValid)
                    {
                        _logger.LogInformation("Validated connection with {sender}, connection is good", sender);

                        //TODO decide if we should throw an error here?
                        return;
                    }
                }
                catch (OdinIdentityVerificationException)
                {
                    // This can occur if the sender does not have access to the ICR
                    //No-op, ignore this issue
                }
            }

            //Check if a request was sent to the sender
            var sentRequest = await GetSentRequestInternal(sender, cn);
            if (null != sentRequest)
            {
                //we can auto-accept
                _logger.LogInformation("Auto-accepting connection request from {sender}", sender);
                //Note: nothing to do here for now because of the MasterKeyAvailableBackgroundService
                // will check this same logic and auto-approve
                // However, if we drop the MasterKeyAvailableBackgroundService - we will need to
                // put in an inbox item to perform the auto-accept
            }

            var request = new PendingConnectionRequestHeader()
            {
                SenderOdinId = odinContext.GetCallerOdinIdOrFail(),
                ReceivedTimestampMilliseconds = UnixTimeUtc.Now(),
                Payload = payload
            };

            UpsertPendingConnectionRequest(request, cn);

            await _mediator.Publish(new ConnectionRequestReceived()
            {
                Sender = request.SenderOdinId,
                Recipient = recipient,
                OdinContext = odinContext,
                DatabaseConnection = cn
            });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets a connection request sent to the specified recipient
        /// </summary>
        /// <returns>Returns the <see cref="ConnectionRequest"/> if one exists, otherwise null</returns>
        public async Task<ConnectionRequest> GetSentRequest(OdinId recipient, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.AssertCanManageConnections();

            return await this.GetSentRequestInternal(recipient, cn);
        }

        /// <summary>
        /// Deletes the sent request record.  If the recipient accepts the request
        /// after it has been delete, the connection will not be established.
        /// 
        /// This does not notify the original recipient
        /// </summary>
        public Task DeleteSentRequest(OdinId recipient, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.AssertCanManageConnections();
            return DeleteSentRequestInternal(recipient, cn);
        }

        private Task DeleteSentRequestInternal(OdinId recipient, DatabaseConnection cn)
        {
            var newKey = MakeSentRequestsKey(recipient);
            var recordFromNewKeyFormat = _sentRequestValueStorage.Get<ConnectionRequest>(cn, newKey);
            if (null != recordFromNewKeyFormat)
            {
                _sentRequestValueStorage.Delete(cn, newKey);
                return Task.CompletedTask;
            }

            try
            {
                //old method
                _sentRequestValueStorage.Delete(cn, recipient.ToHashId());
                return Task.CompletedTask;
            }
            catch (Exception e)
            {
                throw new OdinSystemException("old key lookup method failed", e);
            }
        }

        /// <summary>
        /// Accepts a connection request.  This will store the public key certificate 
        /// of the sender then send the recipients public key certificate to the sender.
        /// </summary>
        public async Task AcceptConnectionRequest(AcceptRequestHeader header, bool tryOverrideAcl, IOdinContext odinContext, DatabaseConnection cn)
        {
            header.Validate();

            var pendingRequest = await GetPendingRequest((OdinId)header.Sender, odinContext, cn);
            if (null == pendingRequest)
            {
                throw new OdinClientException($"No pending request was found from sender [{header.Sender}]");
            }

            pendingRequest.Validate();
            var senderOdinId = (OdinId)pendingRequest.SenderOdinId;
            AccessExchangeGrant accessGrant = null;

            //Note: this option is used for auto-accepting connection requests for the Invitation feature
            if (tryOverrideAcl)
            {
                //If I had previously sent a connection request; use the ACLs I already created
                var existingSentRequest = await GetSentRequestInternal(senderOdinId, cn);
                if (null != existingSentRequest)
                {
                    accessGrant = existingSentRequest.PendingAccessExchangeGrant;
                }
            }

            _logger.LogInformation($"Accept Connection request called for sender {senderOdinId} to {pendingRequest.Recipient}");
            var remoteClientAccessToken = ClientAccessToken.FromPortableBytes64(pendingRequest.ClientAccessToken64);

            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            // Note: We want to use the same shared secret for the identities so let use the shared secret created
            // by the identity who sent the request
            var (accessRegistration, clientAccessTokenReply) = await _exchangeGrantService.CreateClientAccessToken(keyStoreKey,
                ClientTokenType.IdentityConnectionRegistration,
                sharedSecret: remoteClientAccessToken.SharedSecret);

            SensitiveByteArray masterKey = odinContext.Caller.HasMasterKey ? odinContext.Caller.GetMasterKey() : null;
            var circles = header.CircleIds?.ToList() ?? new List<GuidId>();
            accessGrant ??= new AccessExchangeGrant()
            {
                //Note: we allow a null value here in case the connection request process doesn't have 
                MasterKeyEncryptedKeyStoreKey = odinContext.Caller.HasMasterKey ? new SymmetricKeyEncryptedAes(masterKey, keyStoreKey) : null,
                IsRevoked = false,
                CircleGrants = await _circleMembershipService.CreateCircleGrantListWithSystemCircle(
                    keyStoreKey,
                    circles,
                    pendingRequest.ConnectionRequestOrigin,
                    masterKey,
                    odinContext,
                    cn),
                AppGrants = await _cns.CreateAppCircleGrantListWithSystemCircle(keyStoreKey, circles, pendingRequest.ConnectionRequestOrigin, masterKey,
                    odinContext, cn),
                AccessRegistration = accessRegistration
            };

            keyStoreKey.Wipe();

            var verificationHash = _cns.CreateVerificationHash(pendingRequest.VerificationRandomCode, remoteClientAccessToken.SharedSecret);
            var encryptedCat = _icrKeyService.EncryptClientAccessTokenUsingIrcKey(remoteClientAccessToken, odinContext, cn);
            await _cns.Connect(senderOdinId,
                accessGrant,
                token: (encryptedCat,  ??),
            pendingRequest.ContactData,
            pendingRequest.ConnectionRequestOrigin,
            pendingRequest.IntroducerOdinId,
            verificationHash,
            odinContext, cn);

            // Now tell the remote to establish the connection

            ConnectionRequestReply acceptedReq = new()
            {
                SenderOdinId = _tenantContext.HostOdinId,
                ContactData = header.ContactData,
                ClientAccessTokenReply64 = clientAccessTokenReply.ToPortableBytes64(),
                TempKey = pendingRequest.TempRawKey,
                VerificationHash = verificationHash
            };

            var authenticationToken64 = remoteClientAccessToken.ToAuthenticationToken().ToPortableBytes64();

            ApiResponse<NoResultResponse> httpResponse = null;

            try
            {
                await TryRetry.WithDelayAsync(
                    _odinConfiguration.Host.PeerOperationMaxAttempts,
                    _odinConfiguration.Host.PeerOperationDelayMs,
                    CancellationToken.None,
                    async () =>
                    {
                        var json = OdinSystemSerializer.Serialize(acceptedReq);
                        var encryptedPayload = SharedSecretEncryptedPayload.Encrypt(json.ToUtf8ByteArray(), remoteClientAccessToken.SharedSecret);
                        var d = new Dictionary<string, string>()
                        {
                            { OdinHeaderNames.EstablishConnectionAuthToken, authenticationToken64 }
                        };
                        var client = _odinHttpClientFactory.CreateClient<ICircleNetworkRequestHttpClient>(senderOdinId, headers: d);

                        httpResponse = await client.EstablishConnection(encryptedPayload);
                    });
            }
            catch (TryRetryException)
            {
                throw new OdinSystemException($"Failed to establish connection request.  Either response was empty or server returned a failure");
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                if (httpResponse.StatusCode == HttpStatusCode.Forbidden)
                {
                    // The remote server does not have a corresponding outgoing request for this sender
                    throw new OdinClientException("The remote identity does not have a corresponding outgoing request.",
                        OdinClientErrorCode.RemoteServerMissingOutgoingRequest);
                }

                throw new OdinSystemException($"Failed to establish connection request.  Either response was empty or server returned a failure");
            }

            await this.DeleteSentRequestInternal(senderOdinId, cn);
            await this.DeletePendingRequestInternal(senderOdinId, cn);

            try
            {
                _logger.LogDebug("AcceptConnectionRequest - Running SynchronizeChannelFiles");
                await _followerService.SynchronizeChannelFiles(senderOdinId, odinContext, cn, remoteClientAccessToken.SharedSecret);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed while trying to sync channels");
            }

            remoteClientAccessToken.AccessTokenHalfKey.Wipe();
            remoteClientAccessToken.SharedSecret.Wipe();
        }

        /// <summary>
        /// Establishes a connection between two individuals.  This must be called
        /// from a recipient who has accepted a sender's connection request
        /// </summary>
        public async Task EstablishConnection(SharedSecretEncryptedPayload payload, string authenticationToken64, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            // Note: This method runs under the Transit Context because it's called by another identity
            // therefore, all operations that require master key or owner access must have already been completed

            //TODO: need to add a blacklist and other checks to see if we want to accept the request from the incoming DI

            var authToken = ClientAuthenticationToken.FromPortableBytes64(authenticationToken64);

            var originalRequest = await GetSentRequestInternal(odinContext.GetCallerOdinIdOrFail(), cn);

            //Assert that I previously sent a request to the dotIdentity attempting to connected with me
            if (null == originalRequest)
            {
                throw new OdinSecurityException("The original request no longer exists in Sent Requests");
            }

            var recipient = (OdinId)originalRequest.Recipient;

            var (_, sharedSecret) = originalRequest.PendingAccessExchangeGrant.AccessRegistration.DecryptUsingClientAuthenticationToken(authToken);
            var payloadBytes = payload.Decrypt(sharedSecret);

            ConnectionRequestReply reply = OdinSystemSerializer.Deserialize<ConnectionRequestReply>(payloadBytes.ToStringFromUtf8Bytes());

            if (!ByteArrayUtil.EquiByteArrayCompare(originalRequest.VerificationHash, reply.VerificationHash))
            {
                throw new OdinSecurityException("The original request's verification has does not match the reply");
            }

            var remoteClientAccessToken = ClientAccessToken.FromPortableBytes64(reply.ClientAccessTokenReply64);

            EncryptedClientAccessToken encryptedCat = null;
            ClientAccessToken weakToken = null;

            var tempKey = reply.TempKey.ToSensitiveByteArray();
            SensitiveByteArray rawIcrKey;

            //if this is being approved by the owner, then we have the master key
            if (originalRequest.TempEncryptedIcrKey == null)
            {
                rawIcrKey = originalRequest.TempEncryptedIcrKey?.DecryptKeyClone(tempKey);
                encryptedCat = EncryptedClientAccessToken.Encrypt(rawIcrKey, remoteClientAccessToken);
            }
            else
            {
                // we're here because the original request was
                // automatically sent (w/o the owner)

                // ensure the drives do not have read access; only write 
                var anyDrivesHaveRead = originalRequest.PendingAccessExchangeGrant.CircleGrants.Any(cg =>
                    cg.Value.KeyStoreKeyEncryptedDriveGrants.Any(dg => dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Read)));

                if (anyDrivesHaveRead)
                {
                    throw new OdinSecurityException("Cannot read drives with auto-connection");
                }

                weakToken = remoteClientAccessToken;
            }


            await _cns.Connect(reply.SenderOdinId,
                originalRequest.PendingAccessExchangeGrant,
                token: (encryptedCat, weakToken),
                reply.ContactData,
                originalRequest.ConnectionRequestOrigin,
                originalRequest.IntroducerOdinId,
                originalRequest.VerificationHash,
                odinContext,
                cn);

            try
            {
                var feedDriveId = await _driveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive, cn);
                var patchedContext = OdinContextUpgrades.PrepForSynchronizeChannelFiles(odinContext,
                    feedDriveId.GetValueOrDefault(),
                    tempKey,
                    originalRequest.TempEncryptedFeedDriveStorageKey,
                    originalRequest.TempEncryptedIcrKey);

                _logger.LogDebug("EstablishConnection - Running SynchronizeChannelFiles");
                await _followerService.SynchronizeChannelFiles(recipient, patchedContext, cn, sharedSecret);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to sync channel files");
            }

            rawIcrKey?.Wipe();
            tempKey.Wipe();

            await this.DeleteSentRequestInternal(recipient, cn);
            await this.DeletePendingRequestInternal(recipient, cn);

            if (originalRequest.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction)
            {
                await _mediator.Publish(new IntroductionsAcceptedNotification()
                {
                    Recipient = recipient,
                    IntroducerOdinId = originalRequest.IntroducerOdinId.GetValueOrDefault(),
                    OdinContext = odinContext,
                    DatabaseConnection = cn
                });
            }
            else
            {
                await _mediator.Publish(new ConnectionRequestAcceptedNotification()
                {
                    Sender = (OdinId)originalRequest.SenderOdinId,
                    Recipient = recipient,
                    OdinContext = odinContext,
                    DatabaseConnection = cn
                });
            }
        }

        /// <summary>
        /// Deletes a pending request.  This is useful if the user decides to ignore a request.
        /// </summary>
        public Task DeletePendingRequest(OdinId sender, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.AssertCanManageConnections();
            return DeletePendingRequestInternal(sender, cn);
        }

        public async Task<bool> HasPendingOrSentRequest(OdinId identity, IOdinContext odinContext, DatabaseConnection cn)
        {
            var hasPendingRequest = await GetPendingRequest(identity, odinContext, cn);
            if (null != hasPendingRequest)
            {
                return true;
            }

            var hasSentRequest = await GetSentRequest(identity, odinContext, cn);
            if (null != hasSentRequest)
            {
                return true;
            }

            return false;
        }

        private Task DeletePendingRequestInternal(OdinId sender, DatabaseConnection cn)
        {
            var newKey = MakePendingRequestsKey(sender);
            var recordFromNewKeyFormat = _pendingRequestValueStorage.Get<PendingConnectionRequestHeader>(cn, newKey);
            if (null != recordFromNewKeyFormat)
            {
                _pendingRequestValueStorage.Delete(cn, newKey);
                return Task.CompletedTask;
            }

            //try the old key
            try
            {
                _pendingRequestValueStorage.Delete(cn, sender.ToHashId());
                return Task.CompletedTask;
            }
            catch (Exception e)
            {
                throw new OdinSystemException("Failed with old key lookup method.", e);
            }
        }

        private void UpsertSentConnectionRequest(ConnectionRequest request, DatabaseConnection cn)
        {
            request.SenderOdinId = _tenantContext.HostOdinId; //store for when we support multiple domains per identity
            _sentRequestValueStorage.Upsert(cn, MakeSentRequestsKey(new OdinId(request.Recipient)), GuidId.Empty, _sentRequestsDataType, request);
        }

        private void UpsertPendingConnectionRequest(PendingConnectionRequestHeader request, DatabaseConnection cn)
        {
            _pendingRequestValueStorage.Upsert(cn, MakePendingRequestsKey(request.SenderOdinId), GuidId.Empty, _pendingRequestsDataType, request);
        }

        private async Task<ConnectionRequest> GetSentRequestInternal(OdinId recipient, DatabaseConnection cn)
        {
            var result = _sentRequestValueStorage.Get<ConnectionRequest>(cn, MakeSentRequestsKey(recipient));
            return await Task.FromResult(result);
        }

        private Guid MakeSentRequestsKey(OdinId recipient)
        {
            var combined = ByteArrayUtil.Combine(recipient.ToHashId().ToByteArray(), _sentRequestsDataType);
            var bytes = ByteArrayUtil.ReduceSHA256Hash(combined);
            return new Guid(bytes);
        }

        private Guid MakePendingRequestsKey(OdinId sender)
        {
            var combined = ByteArrayUtil.Combine(sender.ToHashId().ToByteArray(), _pendingRequestsDataType);
            var bytes = ByteArrayUtil.ReduceSHA256Hash(combined);
            return new Guid(bytes);
        }

        private async Task HandleConnectionRequestInternalForIdentityOwner(ConnectionRequestHeader header, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.AssertCanManageConnections();
            var masterKey = odinContext.Caller.GetMasterKey();

            var recipient = (OdinId)header.Recipient;

            var incomingRequest = await this.GetPendingRequest(recipient, odinContext, cn);
            if (incomingRequest != null)
            {
                // just accept the request; using the ACL info (header.CircleIds, etc.)
                var ac = new AcceptRequestHeader
                {
                    Sender = recipient,
                    CircleIds = header.CircleIds,
                    ContactData = header.ContactData
                };

                await this.AcceptConnectionRequest(ac, tryOverrideAcl: false, odinContext, cn);
                return;
            }

            var existingOutgoingRequest = await this.GetSentRequestInternal(recipient, cn);
            if (null == existingOutgoingRequest)
            {
                await CreateAndSendRequestInternal(header, masterKey, odinContext, cn);
            }
            else
            {
                if (existingOutgoingRequest.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction)
                {
                    //overwrite this with new request and send it
                    await CreateAndSendRequestInternal(header, masterKey, odinContext, cn);
                }
                else if (existingOutgoingRequest.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwner)
                {
                    //merge with existing request
                    var newCircles = header.CircleIds ?? [];
                    var exitingCircles = existingOutgoingRequest.PendingAccessExchangeGrant.CircleGrants.Keys.Select(c => new GuidId(c)).ToList();
                    newCircles.AddRange(exitingCircles.Where(c => !newCircles.Exists(nc => nc == c)).ToList());
                    header.CircleIds = newCircles;
                    await CreateAndSendRequestInternal(header, masterKey, odinContext, cn);
                }
            }
        }

        private async Task HandleConnectionRequestInternalForIntroduction(ConnectionRequestHeader header, IOdinContext odinContext, DatabaseConnection cn)
        {
            OdinValidationUtils.AssertNotNullOrEmpty(header.IntroducerOdinId, nameof(header.IntroducerOdinId));

            var recipient = (OdinId)header.Recipient;

            if (_tenantContext.Settings.AutoAcceptIntroductions)
            {
                var incomingRequest = await this.GetPendingRequest(recipient, odinContext, cn);
                if (incomingRequest == null)
                {
                    await CreateAndSendRequestInternal(header, masterKey: null, odinContext, cn);
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

                    await this.AcceptConnectionRequest(ac, tryOverrideAcl: false, odinContext, cn);
                }
            }
            else
            {
                var existingOutgoingRequest = await this.GetSentRequestInternal(recipient, cn);
                if (null == existingOutgoingRequest)
                {
                    await CreateAndSendRequestInternal(header, masterKey: null, odinContext, cn);
                }
                else
                {
                    var existingRequestOrigin = existingOutgoingRequest.ConnectionRequestOrigin;
                    if (existingRequestOrigin == ConnectionRequestOrigin.Introduction)
                    {
                        // overwrite this with newly incoming request and send it
                        // reason: the new request is created by the owner, so it will more explicit info (like circles)
                        await CreateAndSendRequestInternal(header, masterKey: null, odinContext, cn);
                    }
                    else if (existingRequestOrigin == ConnectionRequestOrigin.IdentityOwner)
                    {
                        // Resend the request - using the circles from the existing request
                        header.CircleIds = existingOutgoingRequest.PendingAccessExchangeGrant.CircleGrants.Keys.Select(c => new GuidId(c)).ToList();
                        await CreateAndSendRequestInternal(header, masterKey: null, odinContext, cn);
                    }
                }
            }
        }

        private async Task CreateAndSendRequestInternal(ConnectionRequestHeader header, SensitiveByteArray masterKey, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var circles = header.CircleIds?.ToList() ?? new List<GuidId>();

            var (clientAccessToken, grant) = await CreateTokenAndExchangeGrant(keyStoreKey, circles, header.ConnectionRequestOrigin,
                masterKey, odinContext, cn);

            var tempRawKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var outgoingRequest = new ConnectionRequest
            {
                Id = header.Id,
                ContactData = header.ContactData,
                Recipient = header.Recipient,
                Message = header.Message,
                ClientAccessToken64 = clientAccessToken.ToPortableBytes64(),
                TempRawKey = tempRawKey.GetKey(), //give the key to the recipient, so they can give it back when they accept the request
                VerificationRandomCode = ByteArrayUtil.GetRandomCryptoGuid(),
                ConnectionRequestOrigin = header.ConnectionRequestOrigin,
                IntroducerOdinId = header.IntroducerOdinId
            };

            await TrySendRequestInternal((OdinId)header.Recipient, outgoingRequest, odinContext, cn);

            outgoingRequest.VerificationHash = _cns.CreateVerificationHash(outgoingRequest.VerificationRandomCode, clientAccessToken.SharedSecret);

            clientAccessToken.SharedSecret.Wipe();
            clientAccessToken.AccessTokenHalfKey.Wipe();

            //
            // Note: These items are set after we send the request, so we can store them locally only
            //
            outgoingRequest.ClientAccessToken64 = "";
            outgoingRequest.PendingAccessExchangeGrant = grant;

            var feedDriveId = odinContext.PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);
            var feedDriveStorageKey = odinContext.PermissionsContext.GetDriveStorageKey(feedDriveId);
            outgoingRequest.TempEncryptedFeedDriveStorageKey = new SymmetricKeyEncryptedAes(tempRawKey, feedDriveStorageKey);

            if (header.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwner)
            {
                //expect a master key
                outgoingRequest.TempEncryptedIcrKey = _icrKeyService.ReEncryptIcrKey(tempRawKey, masterKey, cn);
            }

            keyStoreKey.Wipe();
            tempRawKey.Wipe();
            outgoingRequest.TempRawKey.Wipe();

            UpsertSentConnectionRequest(outgoingRequest, cn);
        }

        private async Task<(ClientAccessToken clientAccessToken, AccessExchangeGrant)> CreateTokenAndExchangeGrant(
            SensitiveByteArray keyStoreKey,
            List<GuidId> circles,
            ConnectionRequestOrigin origin,
            SensitiveByteArray masterKey,
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var (accessRegistration, clientAccessToken) = await _exchangeGrantService.CreateClientAccessToken(
                keyStoreKey,
                ClientTokenType.IdentityConnectionRegistration);

            var grant = new AccessExchangeGrant()
            {
                // We allow this to be null in the case of connection requests coming due to an introduction
                MasterKeyEncryptedKeyStoreKey = masterKey == null ? null : new SymmetricKeyEncryptedAes(masterKey, keyStoreKey),
                IsRevoked = false,
                CircleGrants = await _circleMembershipService.CreateCircleGrantListWithSystemCircle(
                    keyStoreKey,
                    circles,
                    origin,
                    masterKey,
                    odinContext,
                    cn),
                AppGrants = await _cns.CreateAppCircleGrantListWithSystemCircle(keyStoreKey, circles, origin, masterKey, odinContext, cn),
                AccessRegistration = accessRegistration
            };

            return (clientAccessToken, grant);
        }

        private async Task TrySendRequestInternal(OdinId recipient, ConnectionRequest request, IOdinContext odinContext, DatabaseConnection cn)
        {
            async Task<ApiResponse<NoResultResponse>> Send()
            {
                var payloadBytes = OdinSystemSerializer.Serialize(request).ToUtf8ByteArray();
                var rsaEncryptedPayload = await _publicPrivateKeyService.RsaEncryptPayloadForRecipient(PublicPrivateKeyType.OnlineKey,
                    recipient, payloadBytes, cn);

                var token = await ResolveClientAccessToken(recipient, odinContext, cn, false);
                var client = token == null
                    ? _odinHttpClientFactory.CreateClient<ICircleNetworkRequestHttpClient>(recipient)
                    : _odinHttpClientFactory.CreateClientUsingAccessToken<ICircleNetworkRequestHttpClient>(recipient, token.ToAuthenticationToken());

                var response = await client.DeliverConnectionRequest(rsaEncryptedPayload);
                return response;
            }

            var response = await Send();
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new OdinSecurityException("Remote server denied connection");
            }

            if (!response.IsSuccessStatusCode && response.Content!.Success)
            {
                //public key might be invalid, destroy the cache item
                await _publicPrivateKeyService.InvalidateRecipientRsaPublicKey(recipient, cn);

                var response2 = await Send();
                if (response2.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new OdinSecurityException("Remote server denied connection");
                }

                if (!response2.IsSuccessStatusCode && response2.Content!.Success)
                {
                    throw new OdinClientException("Failed to establish connection request");
                }
            }

            // return response;
        }
    }
}