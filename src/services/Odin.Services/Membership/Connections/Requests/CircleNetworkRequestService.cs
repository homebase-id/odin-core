using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Table;
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
        private static readonly byte[] PendingRequestsDataType = Guid.Parse("e8597025-97b8-4736-8f6c-76ae696acd86").ToByteArray();
        private static readonly byte[] SentRequestsDataType = Guid.Parse("32130ad3-d8aa-445a-a932-162cb4d499b4").ToByteArray();

        private const string PendingContextKey = "11e5788a-8117-489e-9412-f2ab2978b46d";

        private readonly ThreeKeyValueStorage _pendingRequestValueStorage =
            TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(PendingContextKey));

        private const string SentContextKey = "27a49f56-dd00-4383-bf5e-cd94e3ac193b";

        private readonly ThreeKeyValueStorage _sentRequestValueStorage =
            TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(SentContextKey));

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
        private readonly TableKeyThreeValue _tblKeyThreeValue;
        private readonly ITenantLevel2Cache<CircleNetworkRequestService> _cache;

        public CircleNetworkRequestService(
            CircleNetworkService cns,
            ILogger<CircleNetworkRequestService> logger,
            IOdinHttpClientFactory odinHttpClientFactory,
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
            OdinConfiguration odinConfiguration,
            TableKeyThreeValue tblKeyThreeValue,
            ITenantLevel2Cache<CircleNetworkRequestService> cache)
            : base(odinHttpClientFactory, cns, fileSystemResolver, odinConfiguration)
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
            _tblKeyThreeValue = tblKeyThreeValue;
            _cache = cache;
        }

        /// <summary>
        /// Gets a pending request by its sender
        /// </summary>
        public async Task<ConnectionRequest> GetPendingRequestAsync(OdinId sender, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
            var header = await _pendingRequestValueStorage.GetAsync<PendingConnectionRequestHeader>(_tblKeyThreeValue,
                MakePendingRequestsKey(sender));

            if (null == header)
            {
                return null;
            }

            bool isValidPublicKey;
            byte[] payloadBytes;

            // Updated to fall back to ecc encryption until we fully remove RSA
            if (null == header.Payload)
            {
                if (null == header.EccEncryptedPayload)
                {
                    _logger.LogDebug($"RSA Payload for incoming/pending request from {sender} was null");
                    return null;
                }

                payloadBytes = await _publicPrivateKeyService.EccDecryptPayload(header.EccEncryptedPayload, odinContext);
            }
            else
            {
                (isValidPublicKey, payloadBytes) =
                    await _publicPrivateKeyService.RsaDecryptPayloadAsync(PublicPrivateKeyType.OnlineKey, header.Payload, odinContext);

                if (isValidPublicKey == false)
                {
                    throw new OdinClientException("Invalid or expired public key", OdinClientErrorCode.InvalidOrExpiredRsaKey);
                }
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
            var results = await _pendingRequestValueStorage.GetByCategoryAsync<PendingConnectionRequestHeader>(_tblKeyThreeValue,
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
            var results = await _sentRequestValueStorage.GetByCategoryAsync<ConnectionRequest>(_tblKeyThreeValue, SentRequestsDataType);
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

            if (existingConnection.IsConnected() && header.ConnectionRequestOrigin == ConnectionRequestOrigin.IdentityOwner)
            {
                if ((await _verificationService.VerifyConnectionAsync(recipient, cancellationToken, odinContext)).IsValid)
                {
                    // connection is good
                    throw new OdinClientException("Cannot send connection request to a valid connection",
                        OdinClientErrorCode.CannotSendConnectionRequestToValidConnection);
                }
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
        /// Stores a new pending/incoming request that is not yet accepted.
        /// </summary>
        public async Task ReceiveConnectionRequestAsync(EccEncryptedPayload payload, CancellationToken cancellationToken,
            IOdinContext odinContext)
        {
            odinContext.Caller.AssertCallerIsAuthenticated();

            //TODO: check robot detection code

            if (!await _publicPrivateKeyService.IsValidEccPublicKeyAsync(payload.KeyType, payload.EncryptionPublicKeyCrc32))
            {
                throw new OdinClientException("Encrypted Payload Public Key does not match recipient",
                    OdinClientErrorCode.PublicKeyEncryptionIsInvalid);
            }

            var sender = odinContext.GetCallerOdinIdOrFail();
            var recipient = _tenantContext.HostOdinId;

            var existingConnection = await _cns.GetIcrAsync(sender, odinContext, true);
            if (existingConnection.Status == ConnectionStatus.Blocked)
            {
                throw new OdinSecurityException("Identity is blocked");
            }

            var outgoingTimestamp = await _cache.TryGetAsync<Guid>(CacheKey(sender), cancellationToken);
            if (outgoingTimestamp.HasValue)
            {
                //who short first?  if mine was sent first
                if (ByteArrayUtil.muidcmp(outgoingTimestamp, payload.TimestampId) == -1)
                {
                    throw new OdinClientException("Introductory request already sent", OdinClientErrorCode.IntroductoryRequestAlreadySent);
                }
            }

            var request = new PendingConnectionRequestHeader()
            {
                SenderOdinId = odinContext.GetCallerOdinIdOrFail(),
                ReceivedTimestampMilliseconds = UnixTimeUtc.Now(),
                EccEncryptedPayload = payload
            };

            await UpsertPendingConnectionRequestAsync(request);
            
            await _mediator.Publish(new ConnectionRequestReceivedNotification()
            {
                Sender = request.SenderOdinId,
                Recipient = recipient,
                OdinContext = odinContext,
                Request = request,
            }, cancellationToken);
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
        /// after it has been delete, the connection will not be established.
        /// 
        /// This does not notify the original recipient
        /// </summary>
        public Task DeleteSentRequest(OdinId recipient, IOdinContext odinContext)
        {
            odinContext.AssertCanManageConnections();
            return DeleteSentRequestInternalAsync(recipient);
        }

        private async Task DeleteSentRequestInternalAsync(OdinId recipient)
        {
            var newKey = MakeSentRequestsKey(recipient);
            var recordFromNewKeyFormat = await _sentRequestValueStorage.GetAsync<ConnectionRequest>(_tblKeyThreeValue, newKey);
            if (null != recordFromNewKeyFormat)
            {
                await _sentRequestValueStorage.DeleteAsync(_tblKeyThreeValue, newKey);
                return;
            }

            try
            {
                //old method
                await _sentRequestValueStorage.DeleteAsync(_tblKeyThreeValue, recipient.ToHashId());
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
        public async Task AcceptConnectionRequestAsync(AcceptRequestHeader header, bool tryOverrideAcl, IOdinContext odinContext)
        {
            header.Validate();

            var incomingRequest = await GetPendingRequestAsync((OdinId)header.Sender, odinContext);
            if (null == incomingRequest)
            {
                throw new OdinClientException($"No pending request was found from sender [{header.Sender}]", OdinClientErrorCode.IncomingRequestNotFound);
            }

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

            _logger.LogInformation($"Accept Connection request called for sender {senderOdinId} to {incomingRequest.Recipient}");
            var remoteClientAccessToken = ClientAccessToken.FromPortableBytes64(incomingRequest.ClientAccessToken64);

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
                MasterKeyEncryptedKeyStoreKey =
                    odinContext.Caller.HasMasterKey ? new SymmetricKeyEncryptedAes(masterKey, keyStoreKey) : null,
                IsRevoked = false,
                CircleGrants = await _circleMembershipService.CreateCircleGrantListWithSystemCircleAsync(
                    keyStoreKey,
                    circles,
                    incomingRequest.ConnectionRequestOrigin,
                    masterKey,
                    odinContext),
                AppGrants = await _cns.CreateAppCircleGrantListWithSystemCircle(keyStoreKey, circles,
                    incomingRequest.ConnectionRequestOrigin, masterKey, odinContext),
                AccessRegistration = accessRegistration
            };

            var verificationHash = _cns.CreateVerificationHash(
                incomingRequest.VerificationRandomCode,
                remoteClientAccessToken.SharedSecret);

            EncryptedClientAccessToken encryptedCat = null;
            (EccEncryptedPayload Token, EccEncryptedPayload KeyStoreKey) eccEncryptedKeys = default;

            if (odinContext.Caller.HasMasterKey)
            {
                //TODO: read ICR key from app?
                encryptedCat = await _icrKeyService.EncryptClientAccessTokenUsingIrcKeyAsync(remoteClientAccessToken, odinContext);
            }
            else
            {
                //TODO: should we validate all drives are write-only ?
                var keyType = GetPublicPrivateKeyType(incomingRequest.ConnectionRequestOrigin);
                var eccEncryptedCat = await _publicPrivateKeyService.EccEncryptPayload(
                    keyType,
                    remoteClientAccessToken.ToPortableBytes());

                var eccEncryptedKeyStoreKey = await _publicPrivateKeyService.EccEncryptPayload(keyType,
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
                SenderOdinId = _tenantContext.HostOdinId,
                ContactData = header.ContactData,
                ClientAccessTokenReply64 = clientAccessTokenReply.ToPortableBytes64(),
                TempKey = incomingRequest.TempRawKey,
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
                        var encryptedPayload =
                            SharedSecretEncryptedPayload.Encrypt(json.ToUtf8ByteArray(), remoteClientAccessToken.SharedSecret);
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
                throw new OdinSystemException("Failed to establish connection request.  Either " +
                                              "response was empty or server returned a failure");
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
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

            try
            {
                _logger.LogDebug("AcceptConnectionRequest - Running SynchronizeChannelFiles");
                await _followerService.SynchronizeChannelFilesAsync(senderOdinId, odinContext, remoteClientAccessToken.SharedSecret);
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
        public async Task EstablishConnection(SharedSecretEncryptedPayload payload, string authenticationToken64, IOdinContext odinContext)
        {
            // Note: This method runs under the Transit Context because it's called by another identity
            // therefore, all operations that require master key or owner access must have already been completed

            //TODO: need to add a blacklist and other checks to see if we want to accept the request from the incoming DI

            var authToken = ClientAuthenticationToken.FromPortableBytes64(authenticationToken64);
            var caller = odinContext.GetCallerOdinIdOrFail();
            var originalRequest = await GetSentRequestInternalAsync(caller);

            //Assert that I previously sent a request to the identity attempting to connected with me
            if (null == originalRequest)
            {
                if (await _cache.ContainsAsync(CacheKey(caller)))
                {
                    // I have an outgoing request to the caller while the caller is trying to establish a connection with me
                    // this will always be true due to the fact the record is removed AFTER the request is sent AND the fact the 
                    // establish connection is called as part of the outgoing request.
                }
                
                // this can also happen if the connection was already approved via auto-accept 
                var existingConnection = await _cns.GetIcrAsync(caller, odinContext, true);
                if (existingConnection.IsConnected() && existingConnection.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction)
                {
                    _logger.LogDebug("Ignoring EstablishConnection from {caller}. Already connected via introduction", caller);
                    return;
                }

                throw new OdinSecurityException("The original request no longer exists in Sent Requests");
            }

            var recipient = (OdinId)originalRequest.Recipient;

            var (keyStoreKey, sharedSecret) = originalRequest.PendingAccessExchangeGrant
                .AccessRegistration.DecryptUsingClientAuthenticationToken(authToken);
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

                var eccEncryptedCat = await _publicPrivateKeyService.EccEncryptPayload(keyType,
                    remoteClientAccessToken.ToPortableBytes());

                var eccEncryptedKeyStoreKey = await _publicPrivateKeyService.EccEncryptPayload(keyType,
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
                    var feedDriveId = await _driveManager.GetDriveIdByAliasAsync(SystemDriveConstants.FeedDrive);
                    var patchedContext = OdinContextUpgrades.PrepForSynchronizeChannelFiles(odinContext,
                        feedDriveId.GetValueOrDefault(),
                        tempKey,
                        originalRequest.TempEncryptedFeedDriveStorageKey,
                        originalRequest.TempEncryptedIcrKey);

                    _logger.LogDebug("EstablishConnection - Running SynchronizeChannelFiles");
                    await _followerService.SynchronizeChannelFilesAsync(recipient, patchedContext, sharedSecret);
                }
                else
                {
                    _logger.LogDebug("skipping Feed drive sync since no temp feed drive storage key was available");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to sync channel files");
            }

            rawIcrKey?.Wipe();
            tempKey.Wipe();

            await this.DeleteSentRequestInternalAsync(recipient);
            await this.DeletePendingRequestInternal(recipient);

            if (originalRequest.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction)
            {
                await _mediator.Publish(new IntroductionsAcceptedNotification()
                {
                    Recipient = recipient,
                    IntroducerOdinId = originalRequest.IntroducerOdinId.GetValueOrDefault(),
                    OdinContext = odinContext,
                });
            }
            else
            {
                await _mediator.Publish(new ConnectionRequestAcceptedNotification()
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
            odinContext.AssertCanManageConnections();
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
            var header = await _pendingRequestValueStorage.GetAsync<PendingConnectionRequestHeader>(_tblKeyThreeValue,
                MakePendingRequestsKey(sender));
            return header != null;
        }

        private async Task DeletePendingRequestInternal(OdinId sender)
        {
            var newKey = MakePendingRequestsKey(sender);
            var recordFromNewKeyFormat = await _pendingRequestValueStorage
                .GetAsync<PendingConnectionRequestHeader>(_tblKeyThreeValue, newKey);

            if (null != recordFromNewKeyFormat)
            {
                await _pendingRequestValueStorage.DeleteAsync(_tblKeyThreeValue, newKey);
                return;
            }

            //try the old key
            try
            {
                await _pendingRequestValueStorage.DeleteAsync(_tblKeyThreeValue, sender.ToHashId());
            }
            catch (Exception e)
            {
                throw new OdinSystemException("Failed with old key lookup method.", e);
            }
        }

        private async Task UpsertSentConnectionRequestAsync(ConnectionRequest request)
        {
            request.SenderOdinId = _tenantContext.HostOdinId; //store for when we support multiple domains per identity
            await _sentRequestValueStorage.UpsertAsync(_tblKeyThreeValue, MakeSentRequestsKey(new OdinId(request.Recipient)), GuidId.Empty,
                SentRequestsDataType,
                request);
        }

        private async Task UpsertPendingConnectionRequestAsync(PendingConnectionRequestHeader request)
        {
            await _pendingRequestValueStorage.UpsertAsync(_tblKeyThreeValue, MakePendingRequestsKey(request.SenderOdinId), GuidId.Empty,
                PendingRequestsDataType,
                request);
        }

        private async Task<ConnectionRequest> GetSentRequestInternalAsync(OdinId recipient)
        {
            var result = await _sentRequestValueStorage.GetAsync<ConnectionRequest>(_tblKeyThreeValue, MakeSentRequestsKey(recipient));
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

        private async Task HandleConnectionRequestInternalForIdentityOwnerAsync(ConnectionRequestHeader header, IOdinContext odinContext)
        {
            odinContext.AssertCanManageConnections();
            var masterKey = odinContext.Caller.GetMasterKey();

            var recipient = (OdinId)header.Recipient;

            _logger.LogDebug("Sending Identity-owner-connection request to {recipient}", recipient);

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
                await CreateAndSendRequestInternalAsync(header, masterKey, odinContext);
            }
            else
            {
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

            _logger.LogDebug("Sending Introduced-connection request to {recipient}", recipient);

            if (_tenantContext.Settings.DisableAutoAcceptIntroductionsForTests)
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

        private async Task CreateAndSendRequestInternalAsync(ConnectionRequestHeader header, SensitiveByteArray masterKey,
            IOdinContext odinContext)
        {
            var recipient = (OdinId)header.Recipient;

            //TODO: scalability - _outgoingIntroductionRequests needs to work across servers
            var timestamp = SequentialGuid.CreateGuid();
            await _cache.SetAsync(CacheKey(recipient), timestamp, TimeSpan.FromHours(1));

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

                var feedDriveId = odinContext.PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);
                var feedDriveStorageKey = odinContext.PermissionsContext.GetDriveStorageKey(feedDriveId);
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

                if (!await TrySendRequestInternalAsync((OdinId)header.Recipient, outgoingRequest, timestamp))
                {
                    await DeleteSentRequestInternalAsync(recipient);
                }
            }
            finally
            {
                await _cache.RemoveAsync(CacheKey(recipient));
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
            var (accessRegistration, clientAccessToken) = await _exchangeGrantService.CreateClientAccessToken(
                keyStoreKey,
                ClientTokenType.IdentityConnectionRegistration);

            var grant = new AccessExchangeGrant()
            {
                // We allow this to be null in the case of connection requests coming due to an introduction
                MasterKeyEncryptedKeyStoreKey = masterKey == null ? null : new SymmetricKeyEncryptedAes(masterKey, keyStoreKey),
                IsRevoked = false,
                CircleGrants = await _circleMembershipService.CreateCircleGrantListWithSystemCircleAsync(
                    keyStoreKey,
                    circles,
                    origin,
                    masterKey,
                    odinContext),
                AppGrants = await _cns.CreateAppCircleGrantListWithSystemCircle(keyStoreKey, circles, origin, masterKey, odinContext),
                AccessRegistration = accessRegistration
            };

            return (clientAccessToken, grant);
        }

        private async Task<bool> TrySendRequestInternalAsync(OdinId recipient, ConnectionRequest request, Guid timestamp)
        {
            var keyType = GetPublicPrivateKeyType(request.ConnectionRequestOrigin);

            async Task<(bool encryptionSucceeded, ApiResponse<HttpContent> deliveryResponse)> Send()
            {
                EccEncryptedPayload eccEncryptedPayload;
                try
                {
                    var payloadBytes = OdinSystemSerializer.Serialize(request).ToUtf8ByteArray();
                    eccEncryptedPayload = await _publicPrivateKeyService.EccEncryptPayloadForRecipientAsync(
                        keyType,
                        recipient,
                        payloadBytes);
                }
                catch (OdinRemoteIdentityException e)
                {
                    _logger.LogInformation(e, "Failed to encrypt payload for recipient");
                    return (false, null);
                }

                var client = _odinHttpClientFactory.CreateClient<ICircleNetworkRequestHttpClient>(recipient);

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
                        return false;
                        // there was already a request sent, bubble this up
                        // throw new OdinClientException("Remote server already sent a request",
                        //     OdinClientErrorCode.IntroductoryRequestAlreadySent);
                    }
                }
            }
            else
            {
                _logger.LogDebug("TrySendRequestInternal to {recipient} failed the first time. " +
                                 "Invalidating public key cache and retrying", recipient);

                await _publicPrivateKeyService.InvalidateRecipientEccPublicKeyAsync(keyType, recipient);
                sendResult1 = await Send();

                if (!sendResult1.encryptionSucceeded)
                {
                    throw new OdinRemoteIdentityException("Failed to encrypt payload for recipient");
                }
            }

            if (sendResult1.deliveryResponse.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new OdinSecurityException("Remote server denied connection");
            }

            if (!sendResult1.deliveryResponse.IsSuccessStatusCode)
            {
                var sendResult2 = await Send();
                if (sendResult2.deliveryResponse.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new OdinSecurityException("Remote server denied connection");
                }

                if (!sendResult2.deliveryResponse.IsSuccessStatusCode)
                {
                    throw new OdinClientException("Failed to establish connection request");
                }
            }

            return true;
        }

        private PublicPrivateKeyType GetPublicPrivateKeyType(ConnectionRequestOrigin origin)
        {
            return origin == ConnectionRequestOrigin.Introduction
                ? PublicPrivateKeyType.OfflineKey
                : PublicPrivateKeyType.OnlineIcrEncryptedKey;
        }

        private static string CacheKey(Guid uuid)
        {
            return "OutgoingIntroductionRequests:" + uuid;
        }
    }
}