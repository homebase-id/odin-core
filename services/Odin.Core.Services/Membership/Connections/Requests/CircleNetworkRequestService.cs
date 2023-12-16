using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Mediator;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Core.Storage;
using Odin.Core.Time;
using Refit;

namespace Odin.Core.Services.Membership.Connections.Requests
{
    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public class CircleNetworkRequestService
    {
        private readonly byte[] _pendingRequestsDataType = Guid.Parse("e8597025-97b8-4736-8f6c-76ae696acd86").ToByteArray();

        private readonly byte[] _sentRequestsDataType = Guid.Parse("32130ad3-d8aa-445a-a932-162cb4d499b4").ToByteArray();

        private readonly OdinContextAccessor _contextAccessor;
        private readonly CircleNetworkService _cns;
        private readonly ILogger<CircleNetworkRequestService> _logger;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;

        private readonly IMediator _mediator;
        private readonly TenantContext _tenantContext;
        private readonly PublicPrivateKeyService _publicPrivateKeyService;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly IcrKeyService _icrKeyService;

        private readonly CircleMembershipService _circleMembershipService;

        private readonly ThreeKeyValueStorage _pendingRequestValueStorage;
        private readonly ThreeKeyValueStorage _sentRequestValueStorage;

        public CircleNetworkRequestService(
            OdinContextAccessor contextAccessor,
            CircleNetworkService cns,
            ILogger<CircleNetworkRequestService> logger,
            IOdinHttpClientFactory odinHttpClientFactory,
            TenantSystemStorage tenantSystemStorage,
            IMediator mediator,
            TenantContext tenantContext,
            PublicPrivateKeyService publicPrivateKeyService,
            ExchangeGrantService exchangeGrantService, IcrKeyService icrKeyService, CircleMembershipService circleMembershipService)
        {
            _contextAccessor = contextAccessor;
            _cns = cns;
            _logger = logger;
            _odinHttpClientFactory = odinHttpClientFactory;
            _mediator = mediator;
            _tenantContext = tenantContext;
            _publicPrivateKeyService = publicPrivateKeyService;
            _exchangeGrantService = exchangeGrantService;
            _icrKeyService = icrKeyService;
            _circleMembershipService = circleMembershipService;
            _contextAccessor = contextAccessor;

            const string pendingContextKey = "11e5788a-8117-489e-9412-f2ab2978b46d";
            _pendingRequestValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(pendingContextKey));

            const string sentContextKey = "27a49f56-dd00-4383-bf5e-cd94e3ac193b";
            _sentRequestValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(sentContextKey));
        }

        /// <summary>
        /// Gets a pending request by its sender
        /// </summary>
        public async Task<ConnectionRequest> GetPendingRequest(OdinId sender)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();
            var header = _pendingRequestValueStorage.Get<PendingConnectionRequestHeader>(MakePendingRequestsKey(sender));

            if (null == header)
            {
                return null;
            }

            if (null == header.Payload)
            {
                _logger.LogWarning($"RSA Payload for incoming/pending request from {sender} was null");
                return null;
            }

            var (isValidPublicKey, payloadBytes) = await _publicPrivateKeyService.RsaDecryptPayload(RsaKeyType.OnlineKey, header.Payload);
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
        public async Task<PagedResult<PendingConnectionRequestHeader>> GetPendingRequests(PageOptions pageOptions)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
            var results = _pendingRequestValueStorage.GetByCategory<PendingConnectionRequestHeader>(_pendingRequestsDataType);
            return await Task.FromResult(new PagedResult<PendingConnectionRequestHeader>(pageOptions, 1, results.Select(p => p.Redacted()).ToList()));
        }

        /// <summary>
        /// Get outgoing requests awaiting approval by their recipient
        /// </summary>
        /// <returns></returns>
        public async Task<PagedResult<ConnectionRequest>> GetSentRequests(PageOptions pageOptions)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
            var results = _sentRequestValueStorage.GetByCategory<ConnectionRequest>(_sentRequestsDataType);
            return await Task.FromResult(new PagedResult<ConnectionRequest>(pageOptions, 1, results.ToList()));
        }

        /// <summary>
        /// Sends a <see cref="ConnectionRequest"/> as an invitation.
        /// </summary>
        /// <returns></returns>
        public async Task SendConnectionRequest(ConnectionRequestHeader header)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            Guard.Argument(header, nameof(header)).NotNull();
            Guard.Argument(header.Recipient, nameof(header.Recipient)).NotNull();
            Guard.Argument(header.Id, nameof(header.Id)).HasValue();
            Guard.Argument(header.ContactData, nameof(header.ContactData)).NotNull();
            header.ContactData.Validate();

            if (header.Recipient == _contextAccessor.GetCurrent().Caller.OdinId)
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

            var tempRawIcrKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var outgoingRequest = new ConnectionRequest
            {
                Id = header.Id,
                ContactData = header.ContactData,
                Recipient = header.Recipient,
                Message = header.Message,
                ClientAccessToken64 = clientAccessToken.ToPortableBytes64(),
                TempRawKey = tempRawIcrKey.GetKey(),
                TempEncryptedIcrKey = default
            };

            async Task<bool> TrySendRequest()
            {
                var payloadBytes = OdinSystemSerializer.Serialize(outgoingRequest).ToUtf8ByteArray();
                var rsaEncryptedPayload = await _publicPrivateKeyService.EncryptPayloadForRecipient(RsaKeyType.OnlineKey,
                    (OdinId)header.Recipient, payloadBytes);
                var client = _odinHttpClientFactory.CreateClient<ICircleNetworkRequestHttpClient>((OdinId)outgoingRequest.Recipient);
                var response = await client.DeliverConnectionRequest(rsaEncryptedPayload);
                return response.Content is { Success: true } && response.IsSuccessStatusCode;
            }

            if (!await TrySendRequest())
            {
                //public key might be invalid, destroy the cache item
                await _publicPrivateKeyService.InvalidateRecipientPublicKey((OdinId)header.Recipient);

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
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

            outgoingRequest.TempEncryptedIcrKey = _icrKeyService.ReEncryptIcrKey(tempRawIcrKey);
            outgoingRequest.PendingAccessExchangeGrant = new AccessExchangeGrant()
            {
                //TODO: encrypting the key store key here is wierd.  this should be done in the exchange grant service
                MasterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(masterKey, keyStoreKey),
                IsRevoked = false,
                CircleGrants = await _circleMembershipService.CreateCircleGrantListWithSystemCircle(
                    header.CircleIds?.ToList() ?? new List<GuidId>(),
                    keyStoreKey),
                AppGrants = await _cns.CreateAppCircleGrantList(header.CircleIds?.ToList() ?? new List<GuidId>(), keyStoreKey),
                AccessRegistration = accessRegistration
            };

            keyStoreKey.Wipe();
            tempRawIcrKey.Wipe();
            ByteArrayUtil.WipeByteArray(outgoingRequest.TempRawKey);

            UpsertSentConnectionRequest(outgoingRequest);
        }


        /// <summary>
        /// Stores an new pending/incoming request that is not yet accepted.
        /// </summary>
        public async Task ReceiveConnectionRequest(RsaEncryptedPayload payload)
        {
            //HACK - need to figure out how to secure receiving of connection requests from other DIs; this might be robot detection code + the fact they're in the odin network
            //_context.GetCurrent().AssertCanManageConnections();

            //TODO: check robot detection code

            var recipient = _tenantContext.HostOdinId;

            var request = new PendingConnectionRequestHeader()
            {
                SenderOdinId = _contextAccessor.GetCurrent().GetCallerOdinIdOrFail(),
                ReceivedTimestampMilliseconds = UnixTimeUtc.Now(),
                Payload = payload
            };

            UpsertPendingConnectionRequest(request);

            await _mediator.Publish(new ConnectionRequestReceived()
            {
                Sender = request.SenderOdinId,
                Recipient = recipient
            });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets a connection request sent to the specified recipient
        /// </summary>
        /// <returns>Returns the <see cref="ConnectionRequest"/> if one exists, otherwise null</returns>
        public async Task<ConnectionRequest> GetSentRequest(OdinId recipient)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            return await this.GetSentRequestInternal(recipient);
        }

        /// <summary>
        /// Deletes the sent request record.  If the recipient accepts the request
        /// after it has been delete, the connection will not be established.
        /// 
        /// This does not notify the original recipient
        /// </summary>
        /// <param name="recipient"></param>
        public Task DeleteSentRequest(OdinId recipient)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();
            return DeleteSentRequestInternal(recipient);
        }

        private Task DeleteSentRequestInternal(OdinId recipient)
        {
            var newKey = MakeSentRequestsKey(recipient);
            var recordFromNewKeyFormat = _sentRequestValueStorage.Get<ConnectionRequest>(newKey);
            if (null != recordFromNewKeyFormat)
            {
                _sentRequestValueStorage.Delete(newKey);
                return Task.CompletedTask;
            }

            try
            {
                //old method
                _sentRequestValueStorage.Delete(recipient.ToHashId());
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
        public async Task AcceptConnectionRequest(AcceptRequestHeader header)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            Guard.Argument(header, nameof(header)).NotNull();
            header.Validate();

            var pendingRequest = await GetPendingRequest((OdinId)header.Sender);
            Guard.Argument(pendingRequest, nameof(pendingRequest)).NotNull($"No pending request was found from sender [{header.Sender}]");
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
            

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var accessGrant = new AccessExchangeGrant()
            {
                //TODO: encrypting the key store key here is wierd.  this should be done in the exchange grant service
                MasterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(masterKey, keyStoreKey),
                IsRevoked = false,
                CircleGrants = await _circleMembershipService.CreateCircleGrantListWithSystemCircle(header.CircleIds?.ToList() ?? new List<GuidId>(), keyStoreKey),
                AppGrants = await _cns.CreateAppCircleGrantList(header.CircleIds?.ToList() ?? new List<GuidId>(), keyStoreKey),
                AccessRegistration = accessRegistration
            };
            keyStoreKey.Wipe();

            var encryptedCat = _icrKeyService.EncryptClientAccessTokenUsingIrcKey(remoteClientAccessToken);
            await _cns.Connect(senderOdinId, accessGrant, encryptedCat, pendingRequest.ContactData);

            
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
            
            
            
            
            await this.DeletePendingRequest(senderOdinId);
            await this.DeleteSentRequest(senderOdinId);

            // At this point connection is complete; on both identities, therefore - 
            //  I can call the senderIdentity indicating everything is finalized
            //  and it, in turn can perform any operations such as - populating my feed with historical content
            await NotifyConnectionProcessComplete(senderOdinId, remoteClientAccessToken);

            remoteClientAccessToken.AccessTokenHalfKey.Wipe();
            remoteClientAccessToken.SharedSecret.Wipe();
        }

        /// <summary>
        /// Establishes a connection between two individuals.  This must be called
        /// from a recipient who has accepted a sender's connection request
        /// </summary>
        public async Task EstablishConnection(SharedSecretEncryptedPayload payload, string authenticationToken64)
        {
            // Note: This method runs under the Transit Context because it's called by another identity
            // therefore, all operations that require master key or owner access must have already been completed

            //TODO: need to add a blacklist and other checks to see if we want to accept the request from the incoming DI
            var authToken = ClientAuthenticationToken.FromPortableBytes64(authenticationToken64);

            var originalRequest = await GetSentRequestInternal(_contextAccessor.GetCurrent().GetCallerOdinIdOrFail());

            //Assert that I previously sent a request to the dotIdentity attempting to connected with me
            if (null == originalRequest)
            {
                throw new InvalidOperationException("The original request no longer exists in Sent Requests");
            }

            var (_, sharedSecret) = originalRequest.PendingAccessExchangeGrant.AccessRegistration.DecryptUsingClientAuthenticationToken(authToken);
            var payloadBytes = payload.Decrypt(sharedSecret);

            ConnectionRequestReply reply = OdinSystemSerializer.Deserialize<ConnectionRequestReply>(payloadBytes.ToStringFromUtf8Bytes());

            var remoteClientAccessToken = ClientAccessToken.FromPortableBytes64(reply.ClientAccessTokenReply64);

            var tempKey = reply.TempKey.ToSensitiveByteArray();
            var rawIcrKey = originalRequest.TempEncryptedIcrKey.DecryptKeyClone(tempKey);
            var encryptedCat = EncryptedClientAccessToken.Encrypt(rawIcrKey, remoteClientAccessToken);
            rawIcrKey.Wipe();
            tempKey.Wipe();

            await _cns.Connect(reply.SenderOdinId, originalRequest.PendingAccessExchangeGrant, encryptedCat, reply.ContactData);

            await this.DeleteSentRequestInternal((OdinId)originalRequest.Recipient);
            await this.DeletePendingRequestInternal((OdinId)originalRequest.Recipient);

            await _mediator.Publish(new ConnectionRequestAccepted()
            {
                Sender = (OdinId)originalRequest.SenderOdinId,
                Recipient = (OdinId)originalRequest.Recipient
            });

            await NotifyConnectionProcessComplete((OdinId)reply.SenderOdinId, remoteClientAccessToken);
        }

        /// <summary>
        /// Called when the Connection request is fully established
        /// </summary>
        public async Task FinalizeConnection(SharedSecretEncryptedPayload payload)
        {
            // do i need some sort of key to ensure this is only called once?
            // var ss = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
            // var bytes = payload.Decrypt(ss);
            // OdinSystemSerializer.Deserialize<FinalizeKey>(bytes.ToStringFromUtf8Bytes());

            // so the reason for this is to 
            await _mediator.Publish(new NewConnectionEstablishedNotification()
            {
                OdinId = _contextAccessor.GetCurrent().GetCallerOdinIdOrFail()
            });
        }

        /// <summary>
        /// Deletes a pending request.  This is useful if the user decides to ignore a request.
        /// </summary>
        public Task DeletePendingRequest(OdinId sender)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();
            return DeletePendingRequestInternal(sender);
        }

        private async Task NotifyConnectionProcessComplete(OdinId identity, ClientAccessToken remoteClientAccessToken)
        {
            //call to the identity indicating I've finished things on my end

            var bytes = OdinSystemSerializer.Serialize(new { }).ToUtf8ByteArray();
            var payload = SharedSecretEncryptedPayload.Encrypt(bytes, remoteClientAccessToken.SharedSecret);
            var client = _odinHttpClientFactory.CreateClientUsingAccessToken<ICircleNetworkRequestHttpClient>(
                identity,
                remoteClientAccessToken.ToAuthenticationToken());

            var finalizeConnectionResponse = await client.FinalizeConnection(payload);

            if (!finalizeConnectionResponse.IsSuccessStatusCode)
            {
                //TODO: what happens if this fails?  the feed is not populated, what else
            }
        }

        private Task DeletePendingRequestInternal(OdinId sender)
        {
            var newKey = MakePendingRequestsKey(sender);
            var recordFromNewKeyFormat = _pendingRequestValueStorage.Get<PendingConnectionRequestHeader>(newKey);
            if (null != recordFromNewKeyFormat)
            {
                _pendingRequestValueStorage.Delete(newKey);
                return Task.CompletedTask;
            }

            //try the old key
            try
            {
                _pendingRequestValueStorage.Delete(sender.ToHashId());
                return Task.CompletedTask;
            }
            catch (Exception e)
            {
                throw new OdinSystemException("Failed with old key lookup method.", e);
            }
        }

        private void UpsertSentConnectionRequest(ConnectionRequest request)
        {
            request.SenderOdinId = _tenantContext.HostOdinId; //store for when we support multiple domains per identity
            _sentRequestValueStorage.Upsert(MakeSentRequestsKey(new OdinId(request.Recipient)), GuidId.Empty, _sentRequestsDataType, request);
        }

        private void UpsertPendingConnectionRequest(PendingConnectionRequestHeader request)
        {
            _pendingRequestValueStorage.Upsert(MakePendingRequestsKey(request.SenderOdinId), GuidId.Empty, _pendingRequestsDataType, request);
        }

        private async Task<ConnectionRequest> GetSentRequestInternal(OdinId recipient)
        {
            var result = _sentRequestValueStorage.Get<ConnectionRequest>(MakeSentRequestsKey(recipient));
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
    }
}