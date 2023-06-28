﻿using System;
using System.Collections.Generic;
using System.Linq;
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
using Odin.Core.Services.Contacts.Circle.Membership;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Storage;

namespace Odin.Core.Services.Contacts.Circle.Requests
{
    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public class CircleNetworkRequestService
    {
        private readonly GuidId _pendingRequestsDataType = GuidId.FromString("pnd_requests");

        private readonly GuidId _sentRequestsDataType = GuidId.FromString("sent_requests");

        private readonly OdinContextAccessor _contextAccessor;
        private readonly ICircleNetworkService _cns;
        private readonly ILogger<CircleNetworkRequestService> _logger;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;

        private readonly IMediator _mediator;
        private readonly TenantContext _tenantContext;
        private readonly PublicPrivateKeyService _publicPrivateKeyService;
        private readonly ExchangeGrantService _exchangeGrantService;

        private readonly ThreeKeyValueStorage _pendingRequestValueStorage;
        private readonly ThreeKeyValueStorage _sentRequestValueStorage;

        public CircleNetworkRequestService(
            OdinContextAccessor contextAccessor,
            ICircleNetworkService cns,
            ILogger<CircleNetworkRequestService> logger,
            IOdinHttpClientFactory odinHttpClientFactory,
            TenantSystemStorage tenantSystemStorage,
            IMediator mediator,
            TenantContext tenantContext,
            PublicPrivateKeyService publicPrivateKeyService,
            ExchangeGrantService exchangeGrantService)
        {
            _contextAccessor = contextAccessor;
            _cns = cns;
            _logger = logger;
            _odinHttpClientFactory = odinHttpClientFactory;
            _mediator = mediator;
            _tenantContext = tenantContext;
            _publicPrivateKeyService = publicPrivateKeyService;
            _exchangeGrantService = exchangeGrantService;
            _contextAccessor = contextAccessor;

            _pendingRequestValueStorage = tenantSystemStorage.ThreeKeyValueStorage;
            _sentRequestValueStorage = tenantSystemStorage.ThreeKeyValueStorage;
        }

        /// <summary>
        /// Gets a pending request by its sender
        /// </summary>
        public async Task<ConnectionRequest> GetPendingRequest(OdinId sender)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();
            var header = _pendingRequestValueStorage.Get<PendingConnectionRequestHeader>(sender.ToHashId());

            if (null == header)
            {
                return null;
            }

            var (isValidPublicKey, payloadBytes) = await _publicPrivateKeyService.DecryptPayload(RsaKeyType.OnlineKey, header.Payload);
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
            var results = _pendingRequestValueStorage.GetByKey3<PendingConnectionRequestHeader>(_pendingRequestsDataType);
            return await Task.FromResult(new PagedResult<PendingConnectionRequestHeader>(pageOptions, 1, results.Select(p => p.Redacted()).ToList()));
        }

        /// <summary>
        /// Get outgoing requests awaiting approval by their recipient
        /// </summary>
        /// <returns></returns>
        public async Task<PagedResult<ConnectionRequest>> GetSentRequests(PageOptions pageOptions)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
            var results = _sentRequestValueStorage.GetByKey3<ConnectionRequest>(_sentRequestsDataType);
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

            var incomingRequest = await this.GetPendingRequest((OdinId)header.Recipient);
            if (null != incomingRequest)
            {
                throw new OdinClientException("You already have an incoming request from the recipient.",
                    OdinClientErrorCode.CannotSendConnectionRequestToExistingIncomingRequest);
            }

            var existingRequest = await this.GetSentRequest((OdinId)header.Recipient);
            if (existingRequest != null)
            {
                throw new OdinClientException("You already sent a request to this recipient.",
                    OdinClientErrorCode.CannotSendMultipleConnectionRequestToTheSameIdentity);
            }

            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var (accessRegistration, clientAccessToken) = await _exchangeGrantService.CreateClientAccessToken(keyStoreKey, ClientTokenType.Other);

            var outgoingRequest = new ConnectionRequest
            {
                Id = header.Id,
                ContactData = header.ContactData,
                Recipient = header.Recipient,
                Message = header.Message,
                ClientAccessToken64 = clientAccessToken.ToPortableBytes64()
            };

            var payloadBytes = OdinSystemSerializer.Serialize(outgoingRequest).ToUtf8ByteArray();

            async Task<bool> TrySendRequest()
            {
                var rsaEncryptedPayload = await _publicPrivateKeyService.EncryptPayloadForRecipient(RsaKeyType.OnlineKey, (OdinId)header.Recipient, payloadBytes);
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

            //create a grant per circle
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            outgoingRequest.PendingAccessExchangeGrant = new AccessExchangeGrant()
            {
                //TODO: encrypting the key store key here is wierd.  this should be done in the exchange grant service
                MasterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(ref masterKey, ref keyStoreKey),
                IsRevoked = false,
                CircleGrants = await _cns.CreateCircleGrantList(header.CircleIds?.ToList() ?? new List<GuidId>(), keyStoreKey),
                AppGrants = await _cns.CreateAppCircleGrantList(header.CircleIds?.ToList() ?? new List<GuidId>(), keyStoreKey),
                AccessRegistration = accessRegistration
            };
            keyStoreKey.Wipe();

            UpsertSentConnectionRequest(outgoingRequest);
        }

        /// <summary>
        /// Stores an new incoming request that is not yet accepted.
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
                ReceivedTimestampMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Payload = payload
            };

            _pendingRequestValueStorage.Upsert(request.SenderOdinId.ToHashId(), GuidId.Empty, _pendingRequestsDataType, request);

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
            _sentRequestValueStorage.Delete(recipient.ToHashId());
            return Task.CompletedTask;
        }

        /// <summary>
        /// Accepts a connection request.  This will store the public key certificate 
        /// of the sender then send the recipients public key certificate to the sender.
        /// </summary>
        public async Task AcceptConnectionRequest(AcceptRequestHeader header)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            Guard.Argument(header, nameof(header)).NotNull();
            header.Validate();

            var pendingRequest = await GetPendingRequest((OdinId)header.Sender);
            Guard.Argument(pendingRequest, nameof(pendingRequest)).NotNull($"No pending request was found from sender [{header.Sender}]");
            pendingRequest.Validate();

            _logger.LogInformation($"Accept Connection request called for sender {pendingRequest.SenderOdinId} to {pendingRequest.Recipient}");
            var remoteClientAccessToken = ClientAccessToken.FromPortableBytes64(pendingRequest.ClientAccessToken64);

            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            // Note: We want to use the same shared secret for the identities so let use the shared secret created by the identity who sent the request
            var (accessRegistration, clientAccessTokenReply) = await _exchangeGrantService.CreateClientAccessToken(keyStoreKey,
                ClientTokenType.Other,
                sharedSecret: remoteClientAccessToken.SharedSecret);

            ConnectionRequestReply acceptedReq = new()
            {
                SenderOdinId = _tenantContext.HostOdinId,
                ContactData = header.ContactData,
                ClientAccessTokenReply64 = clientAccessTokenReply.ToPortableBytes64()
            };

            var authenticationToken64 = remoteClientAccessToken.ToAuthenticationToken().ToPortableBytes64();
            
            async Task<bool> TryAcceptRequest()
            {
                var json = OdinSystemSerializer.Serialize(acceptedReq);

                var encryptedPayload = SharedSecretEncryptedPayload.Encrypt(json.ToUtf8ByteArray(), remoteClientAccessToken.SharedSecret);
                var response = await _odinHttpClientFactory.CreateClient<ICircleNetworkRequestHttpClient>((OdinId)pendingRequest.SenderOdinId)
                    .EstablishConnection(encryptedPayload, authenticationToken64);

                return response.Content is { Success: true } && response.IsSuccessStatusCode;
            }

            if (!await TryAcceptRequest())
            {
                //public key might be invalid, destroy the cache item
                // await _rsaKeyService.InvalidateRecipientPublicKey((OdinId)pendingRequest.SenderOdinId);
                if (!await TryAcceptRequest())
                {
                    throw new Exception($"Failed to establish connection request.  Either response was empty or server returned a failure");
                }
            }

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var accessGrant = new AccessExchangeGrant()
            {
                //TODO: encrypting the key store key here is wierd.  this should be done in the exchange grant service
                MasterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(ref masterKey, ref keyStoreKey),
                IsRevoked = false,
                CircleGrants = await _cns.CreateCircleGrantList(header.CircleIds?.ToList() ?? new List<GuidId>(), keyStoreKey),
                AppGrants = await _cns.CreateAppCircleGrantList(header.CircleIds?.ToList() ?? new List<GuidId>(), keyStoreKey),
                AccessRegistration = accessRegistration
            };
            keyStoreKey.Wipe();

            await _cns.Connect(pendingRequest.SenderOdinId, accessGrant, remoteClientAccessToken, pendingRequest.ContactData);

            remoteClientAccessToken.AccessTokenHalfKey.Wipe();
            remoteClientAccessToken.SharedSecret.Wipe();

            await this.DeletePendingRequest((OdinId)pendingRequest.SenderOdinId);
            await this.DeleteSentRequest((OdinId)pendingRequest.SenderOdinId);
        }

        /// <summary>
        /// Establishes a connection between two individuals.  This should be called
        /// from a recipient who has accepted a sender's connection request
        /// </summary>
        public async Task EstablishConnection(SharedSecretEncryptedPayload payload, string authenticationToken64)
        {
            // Note:
            // this method runs under the Transit Context because it's called by another identity
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
            await _cns.Connect(reply.SenderOdinId, originalRequest.PendingAccessExchangeGrant, remoteClientAccessToken, reply.ContactData);

            await this.DeleteSentRequestInternal((OdinId)originalRequest.Recipient);
            await this.DeletePendingRequestInternal((OdinId)originalRequest.Recipient);

            await _mediator.Publish(new ConnectionRequestAccepted()
            {
                Sender = (OdinId)originalRequest.SenderOdinId,
                Recipient = (OdinId)originalRequest.Recipient
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

        private void UpsertSentConnectionRequest(ConnectionRequest request)
        {
            request.SenderOdinId = _tenantContext.HostOdinId;
            _sentRequestValueStorage.Upsert(new OdinId(request.Recipient).ToHashId(), GuidId.Empty, _sentRequestsDataType, request);
        }

        private Task DeletePendingRequestInternal(OdinId sender)
        {
            _pendingRequestValueStorage.Delete(sender.ToHashId());
            return Task.CompletedTask;
        }

        private async Task<ConnectionRequest> GetSentRequestInternal(OdinId recipient)
        {
            var result = _sentRequestValueStorage.Get<ConnectionRequest>(recipient.ToHashId());
            return await Task.FromResult(result);
        }
    }
}