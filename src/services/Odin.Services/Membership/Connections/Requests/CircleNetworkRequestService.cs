using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.CircleMembership;


namespace Odin.Services.Membership.Connections.Requests
{
    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public class CircleNetworkRequestService
    {
        private readonly byte[] _pendingRequestsDataType = Guid.Parse("e8597025-97b8-4736-8f6c-76ae696acd86").ToByteArray();

        private readonly byte[] _sentRequestsDataType = Guid.Parse("32130ad3-d8aa-445a-a932-162cb4d499b4").ToByteArray();


        private readonly CircleNetworkService _dbs;
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
        private readonly ThreeKeyValueStorage _pendingRequestValueStorage;
        private readonly ThreeKeyValueStorage _sentRequestValueStorage;


        public CircleNetworkRequestService(
            CircleNetworkService dbs,
            ILogger<CircleNetworkRequestService> logger,
            IOdinHttpClientFactory odinHttpClientFactory,
            TenantSystemStorage tenantSystemStorage,
            IMediator mediator,
            TenantContext tenantContext,
            PublicPrivateKeyService publicPrivateKeyService,
            ExchangeGrantService exchangeGrantService, IcrKeyService icrKeyService, CircleMembershipService circleMembershipService,
            DriveManager driveManager, FollowerService followerService)
        {
            _dbs = dbs;
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


            const string pendingContextKey = "11e5788a-8117-489e-9412-f2ab2978b46d";
            _pendingRequestValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(pendingContextKey));

            const string sentContextKey = "27a49f56-dd00-4383-bf5e-cd94e3ac193b";
            _sentRequestValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(sentContextKey));
        }

        /// <summary>
        /// Gets a pending request by its sender
        /// </summary>
        public async Task<ConnectionRequest> GetPendingRequestAsync(OdinId sender, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.AssertCanManageConnections();
            var header = await _pendingRequestValueStorage.GetAsync<PendingConnectionRequestHeader>(db, MakePendingRequestsKey(sender));

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
                await _publicPrivateKeyService.RsaDecryptPayloadAsync(PublicPrivateKeyType.OnlineKey, header.Payload, odinContext, db);
            if (isValidPublicKey == false)
            {
                throw new OdinClientException("Invalid or expired public key", OdinClientErrorCode.InvalidOrExpiredRsaKey);
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
        public async Task<PagedResult<PendingConnectionRequestHeader>> GetPendingRequestsAsync(PageOptions pageOptions, IOdinContext odinContext,
            IdentityDatabase db)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
            var results = await _pendingRequestValueStorage.GetByCategoryAsync<PendingConnectionRequestHeader>(db, _pendingRequestsDataType);
            return new PagedResult<PendingConnectionRequestHeader>(pageOptions, 1, results.Select(p => p.Redacted()).ToList());
        }

        /// <summary>
        /// Get outgoing requests awaiting approval by their recipient
        /// </summary>
        /// <returns></returns>
        public async Task<PagedResult<ConnectionRequest>> GetSentRequestsAsync(PageOptions pageOptions, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
            var results = await _sentRequestValueStorage.GetByCategoryAsync<ConnectionRequest>(db, _sentRequestsDataType);
            return new PagedResult<ConnectionRequest>(pageOptions, 1, results.ToList());
        }

        /// <summary>
        /// Sends a <see cref="ConnectionRequest"/> as an invitation.
        /// </summary>
        /// <returns></returns>
        public async Task SendConnectionRequestAsync(ConnectionRequestHeader header, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.AssertCanManageConnections();

            header.ContactData?.Validate();

            if (header.Recipient == odinContext.Caller.OdinId)
            {
                throw new OdinClientException(
                    "I get it, connecting with yourself is critical..yet you sent a connection request to yourself but you are already you",
                    OdinClientErrorCode.ConnectionRequestToYourself);
            }

            // var incomingRequest = await this.GetPendingRequest(recipientOdinId);
            // if (null != incomingRequest)
            // {
            //     throw new OdinClientException("You already have an incoming request from the recipient.",
            //         OdinClientErrorCode.CannotSendConnectionRequestToExistingIncomingRequest);
            // }

            // var existingRequest = await this.GetSentRequest(recipientOdinId);
            // if (existingRequest != null)
            // {
            //     //delete the existing request 
            //
            //      await this.DeleteSentRequest(recipientOdinId);
            //     throw new OdinClientException("You already sent a request to this recipient.",
            //         OdinClientErrorCode.CannotSendMultipleConnectionRequestToTheSameIdentity);
            // }

            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var (accessRegistration, clientAccessToken) = await _exchangeGrantService.CreateClientAccessToken(
                keyStoreKey,
                ClientTokenType.IdentityConnectionRegistration);

            var tempRawKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var outgoingRequest = new ConnectionRequest
            {
                Id = header.Id,
                ContactData = header.ContactData,
                Recipient = header.Recipient,
                Message = header.Message,
                ClientAccessToken64 = clientAccessToken.ToPortableBytes64(),
                TempRawKey = tempRawKey.GetKey(),
                TempEncryptedIcrKey = default,
                TempEncryptedFeedDriveStorageKey = default
            };

            async Task<bool> TrySendRequest()
            {
                var payloadBytes = OdinSystemSerializer.Serialize(outgoingRequest).ToUtf8ByteArray();
                var rsaEncryptedPayload = await _publicPrivateKeyService.RsaEncryptPayloadForRecipientAsync(PublicPrivateKeyType.OnlineKey,
                    (OdinId)header.Recipient, payloadBytes, db);
                var client = _odinHttpClientFactory.CreateClient<ICircleNetworkRequestHttpClient>((OdinId)outgoingRequest.Recipient);
                var response = await client.DeliverConnectionRequest(rsaEncryptedPayload);
                return response.Content is { Success: true } && response.IsSuccessStatusCode;
            }

            if (!await TrySendRequest())
            {
                //public key might be invalid, destroy the cache item
                await _publicPrivateKeyService.InvalidateRecipientRsaPublicKeyAsync((OdinId)header.Recipient, db);

                if (!await TrySendRequest())
                {
                    throw new OdinClientException("Failed to establish connection request");
                }
            }

            clientAccessToken.SharedSecret.Wipe();
            clientAccessToken.AccessTokenHalfKey.Wipe();

            //Note: the pending access reg id attached only AFTER we send the request
            outgoingRequest.ClientAccessToken64 = "";

            // Create a grant per circle
            var masterKey = odinContext.Caller.GetMasterKey();

            var feedDriveId = odinContext.PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);
            var feedDriveStorageKey = odinContext.PermissionsContext.GetDriveStorageKey(feedDriveId);

            outgoingRequest.TempEncryptedIcrKey = await _icrKeyService.ReEncryptIcrKeyAsync(tempRawKey, odinContext);
            outgoingRequest.TempEncryptedFeedDriveStorageKey = new SymmetricKeyEncryptedAes(tempRawKey, feedDriveStorageKey);
            outgoingRequest.PendingAccessExchangeGrant = new AccessExchangeGrant()
            {
                //TODO: encrypting the key store key here is wierd.  this should be done in the exchange grant service
                MasterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(masterKey, keyStoreKey),
                IsRevoked = false,
                CircleGrants = await _circleMembershipService.CreateCircleGrantListWithSystemCircleAsync(
                    header.CircleIds?.ToList() ?? new List<GuidId>(),
                    keyStoreKey, odinContext),
                AppGrants = await _dbs.CreateAppCircleGrantListWithSystemCircle(header.CircleIds?.ToList() ?? new List<GuidId>(), keyStoreKey, odinContext),
                AccessRegistration = accessRegistration
            };

            keyStoreKey.Wipe();
            tempRawKey.Wipe();
            ByteArrayUtil.WipeByteArray(outgoingRequest.TempRawKey);

            await UpsertSentConnectionRequestAsync(outgoingRequest, db);
        }

        /// <summary>
        /// Stores an new pending/incoming request that is not yet accepted.
        /// </summary>
        public async Task ReceiveConnectionRequestAsync(RsaEncryptedPayload payload, IOdinContext odinContext, IdentityDatabase db)
        {
            //HACK - need to figure out how to secure receiving of connection requests from other DIs; this might be robot detection code + the fact they're in the odin network
            //_context.GetCurrent().AssertCanManageConnections();

            //TODO: check robot detection code

            var recipient = _tenantContext.HostOdinId;

            var request = new PendingConnectionRequestHeader()
            {
                SenderOdinId = odinContext.GetCallerOdinIdOrFail(),
                ReceivedTimestampMilliseconds = UnixTimeUtc.Now(),
                Payload = payload
            };

            await UpsertPendingConnectionRequestAsync(request, db);

            await _mediator.Publish(new ConnectionRequestReceived()
            {
                Sender = request.SenderOdinId,
                Recipient = recipient,
                OdinContext = odinContext,
                db = db
            });
        }

        /// <summary>
        /// Gets a connection request sent to the specified recipient
        /// </summary>
        /// <returns>Returns the <see cref="ConnectionRequest"/> if one exists, otherwise null</returns>
        public async Task<ConnectionRequest> GetSentRequest(OdinId recipient, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.AssertCanManageConnections();

            return await this.GetSentRequestInternalAsync(recipient, db);
        }

        /// <summary>
        /// Deletes the sent request record.  If the recipient accepts the request
        /// after it has been delete, the connection will not be established.
        /// 
        /// This does not notify the original recipient
        /// </summary>
        /// <param name="recipient"></param>
        /// <param name="odinContext"></param>
        public Task DeleteSentRequest(OdinId recipient, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.AssertCanManageConnections();
            return DeleteSentRequestInternalAsync(recipient, db);
        }

        private async Task DeleteSentRequestInternalAsync(OdinId recipient, IdentityDatabase db)
        {
            var newKey = MakeSentRequestsKey(recipient);
            var recordFromNewKeyFormat = await _sentRequestValueStorage.GetAsync<ConnectionRequest>(db, newKey);
            if (null != recordFromNewKeyFormat)
            {
                await _sentRequestValueStorage.DeleteAsync(db, newKey);
                return;
            }

            try
            {
                //old method
                await _sentRequestValueStorage.DeleteAsync(db, recipient.ToHashId());
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
        public async Task AcceptConnectionRequestAsync(AcceptRequestHeader header, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();
            header.Validate();

            var pendingRequest = await GetPendingRequestAsync((OdinId)header.Sender, odinContext, db);
            if (null == pendingRequest)
            {
                throw new OdinClientException($"No pending request was found from sender [{header.Sender}]");
            }

            pendingRequest.Validate();

            var senderOdinId = (OdinId)pendingRequest.SenderOdinId;

            _logger.LogInformation($"Accept Connection request called for sender {senderOdinId} to {pendingRequest.Recipient}");
            var remoteClientAccessToken = ClientAccessToken.FromPortableBytes64(pendingRequest.ClientAccessToken64);

            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            // Note: We want to use the same shared secret for the identities so let use the shared secret created
            // by the identity who sent the request
            var (accessRegistration, clientAccessTokenReply) = await _exchangeGrantService.CreateClientAccessToken(keyStoreKey,
                ClientTokenType.IdentityConnectionRegistration,
                sharedSecret: remoteClientAccessToken.SharedSecret);

            var masterKey = odinContext.Caller.GetMasterKey();
            var accessGrant = new AccessExchangeGrant()
            {
                //TODO: encrypting the key store key here is wierd.  this should be done in the exchange grant service
                MasterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(masterKey, keyStoreKey),
                IsRevoked = false,
                CircleGrants = await _circleMembershipService.CreateCircleGrantListWithSystemCircleAsync(header.CircleIds?.ToList() ?? new List<GuidId>(),
                    keyStoreKey, odinContext),
                AppGrants = await _dbs.CreateAppCircleGrantListWithSystemCircle(header.CircleIds?.ToList() ?? new List<GuidId>(), keyStoreKey, odinContext),
                AccessRegistration = accessRegistration
            };
            keyStoreKey.Wipe();

            var encryptedCat = await _icrKeyService.EncryptClientAccessTokenUsingIrcKeyAsync(remoteClientAccessToken, odinContext);
            await _dbs.ConnectAsync(senderOdinId, accessGrant, encryptedCat, pendingRequest.ContactData, odinContext);

            // Now tell the remote to establish the connection

            ConnectionRequestReply acceptedReq = new()
            {
                SenderOdinId = _tenantContext.HostOdinId,
                ContactData = header.ContactData,
                ClientAccessTokenReply64 = clientAccessTokenReply.ToPortableBytes64(),
                TempKey = pendingRequest.TempRawKey
            };

            //
            // TODO: !!!! why is this going across the wire unencrypted >:[
            //

            var authenticationToken64 = remoteClientAccessToken.ToAuthenticationToken().ToPortableBytes64();

            async Task<bool> TryAcceptRequest()
            {
                var json = OdinSystemSerializer.Serialize(acceptedReq);
                var encryptedPayload = SharedSecretEncryptedPayload.Encrypt(json.ToUtf8ByteArray(), remoteClientAccessToken.SharedSecret);
                var client = _odinHttpClientFactory.CreateClient<ICircleNetworkRequestHttpClient>(senderOdinId);

                var response = await client.EstablishConnection(encryptedPayload, authenticationToken64);
                return response.Content is { Success: true } && response.IsSuccessStatusCode;
            }

            if (!await TryAcceptRequest())
            {
                if (!await TryAcceptRequest())
                {
                    throw new OdinSystemException($"Failed to establish connection request.  Either response was empty or server returned a failure");
                }
            }

            await this.DeletePendingRequest(senderOdinId, odinContext, db);
            await this.DeleteSentRequest(senderOdinId, odinContext, db);
            
            try
            {
                _logger.LogDebug("AcceptConnectionRequest - Running SynchronizeChannelFiles");
                await _followerService.SynchronizeChannelFiles(senderOdinId, odinContext, remoteClientAccessToken.SharedSecret);
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
            IdentityDatabase db)
        {
            // Note: This method runs under the Transit Context because it's called by another identity
            // therefore, all operations that require master key or owner access must have already been completed

            //TODO: need to add a blacklist and other checks to see if we want to accept the request from the incoming DI
            var authToken = ClientAuthenticationToken.FromPortableBytes64(authenticationToken64);

            var originalRequest = await GetSentRequestInternalAsync(odinContext.GetCallerOdinIdOrFail(), db);

            //Assert that I previously sent a request to the dotIdentity attempting to connected with me
            if (null == originalRequest)
            {
                throw new InvalidOperationException("The original request no longer exists in Sent Requests");
            }

            var recipient = (OdinId)originalRequest.Recipient;

            var (_, sharedSecret) = originalRequest.PendingAccessExchangeGrant.AccessRegistration.DecryptUsingClientAuthenticationToken(authToken);
            var payloadBytes = payload.Decrypt(sharedSecret);

            ConnectionRequestReply reply = OdinSystemSerializer.Deserialize<ConnectionRequestReply>(payloadBytes.ToStringFromUtf8Bytes());

            var remoteClientAccessToken = ClientAccessToken.FromPortableBytes64(reply.ClientAccessTokenReply64);

            var tempKey = reply.TempKey.ToSensitiveByteArray();
            var rawIcrKey = originalRequest.TempEncryptedIcrKey.DecryptKeyClone(tempKey);
            var encryptedCat = EncryptedClientAccessToken.Encrypt(rawIcrKey, remoteClientAccessToken);

            await _dbs.ConnectAsync(reply.SenderOdinId, originalRequest.PendingAccessExchangeGrant, encryptedCat, reply.ContactData, odinContext);

            try
            {
                var feedDriveId = await _driveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive, db);
                var patchedContext = OdinContextUpgrades.PrepForSynchronizeChannelFiles(odinContext, 
                    feedDriveId.GetValueOrDefault(), 
                    tempKey,
                    originalRequest.TempEncryptedFeedDriveStorageKey,
                    originalRequest.TempEncryptedIcrKey);
                
                _logger.LogDebug("EstablishConnection - Running SynchronizeChannelFiles");
                await _followerService.SynchronizeChannelFiles(recipient, patchedContext, sharedSecret);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to sync channel files");
            }

            rawIcrKey.Wipe();
            tempKey.Wipe();

            await this.DeleteSentRequestInternalAsync(recipient, db);
            await this.DeletePendingRequestInternalAsync(recipient, db);

            await _mediator.Publish(new ConnectionRequestAccepted()
            {
                Sender = (OdinId)originalRequest.SenderOdinId,
                Recipient = recipient,
                OdinContext = odinContext,
                db = db
            });
        }

        /// <summary>
        /// Deletes a pending request.  This is useful if the user decides to ignore a request.
        /// </summary>
        public Task DeletePendingRequest(OdinId sender, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.AssertCanManageConnections();
            return DeletePendingRequestInternalAsync(sender, db);
        }

        private async Task DeletePendingRequestInternalAsync(OdinId sender, IdentityDatabase db)
        {
            var newKey = MakePendingRequestsKey(sender);
            var recordFromNewKeyFormat = await _pendingRequestValueStorage.GetAsync<PendingConnectionRequestHeader>(db, newKey);
            if (null != recordFromNewKeyFormat)
            {
                await _pendingRequestValueStorage.DeleteAsync(db, newKey);
                return;
            }

            //try the old key
            try
            {
                await _pendingRequestValueStorage.DeleteAsync(db, sender.ToHashId());
            }
            catch (Exception e)
            {
                throw new OdinSystemException("Failed with old key lookup method.", e);
            }
        }

        private async Task UpsertSentConnectionRequestAsync(ConnectionRequest request, IdentityDatabase db)
        {
            request.SenderOdinId = _tenantContext.HostOdinId; //store for when we support multiple domains per identity
            await _sentRequestValueStorage.UpsertAsync(db, MakeSentRequestsKey(new OdinId(request.Recipient)), GuidId.Empty, _sentRequestsDataType, request);
        }

        private async Task UpsertPendingConnectionRequestAsync(PendingConnectionRequestHeader request, IdentityDatabase db)
        {
            await _pendingRequestValueStorage.UpsertAsync(db, MakePendingRequestsKey(request.SenderOdinId), GuidId.Empty, _pendingRequestsDataType, request);
        }

        private async Task<ConnectionRequest> GetSentRequestInternalAsync(OdinId recipient, IdentityDatabase db)
        {
            var result = await _sentRequestValueStorage.GetAsync<ConnectionRequest>(db, MakeSentRequestsKey(recipient));
            return result;
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
    }
}