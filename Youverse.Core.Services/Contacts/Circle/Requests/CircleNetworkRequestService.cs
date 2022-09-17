using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Mediator.ClientNotifications;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Contacts.Circle.Requests
{
    public class CircleNetworkRequestService : ICircleNetworkRequestService
    {
        private readonly string _pendingPrefix = "pnd";
        private readonly ByteArrayId _pendingRequestsDataType = ByteArrayId.FromString("pnd_requests");

        private readonly string _sentPrefix = "snt";
        private readonly ByteArrayId _sentRequestsDataType = ByteArrayId.FromString("sent_requests");

        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ICircleNetworkService _cns;
        private readonly ILogger<ICircleNetworkRequestService> _logger;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;

        private readonly ISystemStorage _systemStorage;
        private readonly IMediator _mediator;
        private readonly TenantContext _tenantContext;
        private readonly IPublicKeyService _rsaPublicKeyService;
        private readonly CircleDefinitionService _circleDefinitionService;
        private readonly ExchangeGrantService _exchangeGrantService;

        private readonly ThreeKeyValueStorage _pendingRequestValueStorage;
        private readonly ThreeKeyValueStorage _sentRequestValueStorage;

        public CircleNetworkRequestService(
            DotYouContextAccessor contextAccessor,
            ICircleNetworkService cns, ILogger<ICircleNetworkRequestService> logger,
            IDotYouHttpClientFactory dotYouHttpClientFactory,
            ISystemStorage systemStorage,
            IMediator mediator,
            TenantContext tenantContext,
            IPublicKeyService rsaPublicKeyService,
            ExchangeGrantService exchangeGrantService,
            CircleDefinitionService circleDefinitionService)
        {
            _contextAccessor = contextAccessor;
            _cns = cns;
            _logger = logger;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _systemStorage = systemStorage;
            _mediator = mediator;
            _tenantContext = tenantContext;
            _rsaPublicKeyService = rsaPublicKeyService;
            _exchangeGrantService = exchangeGrantService;
            _circleDefinitionService = circleDefinitionService;
            _contextAccessor = contextAccessor;

            _pendingRequestValueStorage = systemStorage.ThreeKeyValueStorage;
            _sentRequestValueStorage = systemStorage.ThreeKeyValueStorage;
        }

        public async Task<PagedResult<ConnectionRequest>> GetPendingRequests(PageOptions pageOptions)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
            var results = _pendingRequestValueStorage.GetByKey3<ConnectionRequest>(_pendingRequestsDataType);
            return new PagedResult<ConnectionRequest>(pageOptions, 1, results.ToList());
        }

        public async Task<PagedResult<ConnectionRequest>> GetSentRequests(PageOptions pageOptions)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
            var results = _pendingRequestValueStorage.GetByKey3<ConnectionRequest>(_sentRequestsDataType);
            return new PagedResult<ConnectionRequest>(pageOptions, 1, results.ToList());
        }

        public async Task SendConnectionRequest(ConnectionRequestHeader header)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            Guard.Argument(header, nameof(header)).NotNull();
            Guard.Argument(header.Recipient, nameof(header.Recipient)).NotNull().Require(r => r != _contextAccessor.GetCurrent().Caller.DotYouId, s => "Cannot send connection request to yourself");
            Guard.Argument(header.Id, nameof(header.Id)).HasValue();

            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var (accessRegistration, clientAccessToken) = await _exchangeGrantService.CreateClientAccessToken(keyStoreKey, ClientTokenType.Other);

            //TODO: need to encrypt the message as well as the rsa credentials
            var request = new ConnectionRequest
            {
                Id = header.Id,
                Name = header.Name,
                Recipient = header.Recipient,
                Message = header.Message,
                SenderDotYouId = this._tenantContext.HostDotYouId, //this should not be required since it's set on the receiving end
                ReceivedTimestampMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), //this should not be required since it's set on the receiving end
                RSAEncryptedExchangeCredentials = EncryptRequestExchangeCredentials((DotYouIdentity)header.Recipient, clientAccessToken)
            };

            var payloadBytes = DotYouSystemSerializer.Serialize(request).ToUtf8ByteArray();
            var rsaEncryptedPayload = await _rsaPublicKeyService.EncryptPayloadForRecipient(header.Recipient, payloadBytes);
            _logger.LogInformation($"[{request.SenderDotYouId}] is sending a request to the server of [{request.Recipient}]");
            var response = await _dotYouHttpClientFactory.CreateClient<ICircleNetworkRequestHttpClient>((DotYouIdentity)request.Recipient).DeliverConnectionRequest(rsaEncryptedPayload);

            if (response.Content is { Success: false } || response.IsSuccessStatusCode == false)
            {
                //public key might be invalid, destroy the cache item
                await _rsaPublicKeyService.InvalidatePublicKey((DotYouIdentity)header.Recipient);

                rsaEncryptedPayload = await _rsaPublicKeyService.EncryptPayloadForRecipient(header.Recipient, payloadBytes);
                _logger.LogInformation($"[{request.SenderDotYouId}] is sending a request to the server of [{request.Recipient}], <mortal kombat voice> round 2");
                response = await _dotYouHttpClientFactory.CreateClient<ICircleNetworkRequestHttpClient>((DotYouIdentity)request.Recipient).DeliverConnectionRequest(rsaEncryptedPayload);

                //round 2, fail all together
                if (response.Content is { Success: false } || response.IsSuccessStatusCode == false)
                {
                    throw new Exception("Failed to establish connection request");
                }
            }

            clientAccessToken.SharedSecret.Wipe();
            clientAccessToken.AccessTokenHalfKey.Wipe();

            //Note: the pending access reg id attached only AFTER we send the request
            request.RSAEncryptedExchangeCredentials = "";

            //create a grant per circle
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            request.PendingAccessExchangeGrant = new AccessExchangeGrant()
            {
                //TODO: encrypting the key store key here is wierd.  this should be done in the exchange grant service
                MasterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(ref masterKey, ref keyStoreKey),
                IsRevoked = false,
                CircleGrants = await _cns.CreateCircleGrantList(header.CircleIds, keyStoreKey),
                AccessRegistration = accessRegistration
            };
            keyStoreKey.Wipe();

            _sentRequestValueStorage.Upsert(new DotYouIdentity(request.Recipient).ToByteArrayId(), ByteArrayId.Empty, _sentRequestsDataType, request, _sentPrefix);
        }

        public async Task ReceiveConnectionRequest(ConnectionRequest request)
        {
            //HACK - need to figure out how to secure receiving of connection requests from other DIs; this might be robot detection code + the fact they're in the youverse network
            //_context.GetCurrent()/AssertCanManageConnections();

            //TODO: check robot detection code

            var sender = _contextAccessor.GetCurrent().Caller.DotYouId;
            var recipient = _tenantContext.HostDotYouId;

            //note: this would occur during the operation verification process
            request.Validate();
            _logger.LogInformation($"[{recipient}] is receiving a connection request from [{sender}]");

            request.SenderDotYouId = sender;
            _pendingRequestValueStorage.Upsert(sender.ToByteArrayId(), ByteArrayId.Empty, _pendingRequestsDataType, request, _pendingPrefix);

#pragma warning disable CS4014
            //let this happen in the background w/o blocking
            _mediator.Publish(new ConnectionRequestReceived()
            {
                Sender = sender
            });
#pragma warning restore CS4014
        }

        public async Task<ConnectionRequest> GetPendingRequest(DotYouIdentity sender)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();
            var result = _pendingRequestValueStorage.Get<ConnectionRequest>(sender.ToByteArrayId(), _pendingPrefix);
            return result;
        }

        public async Task<ConnectionRequest> GetSentRequest(DotYouIdentity recipient)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            return await this.GetSentRequestInternal(recipient);
        }

        public Task DeleteSentRequest(DotYouIdentity recipient)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();
            return DeleteSentRequestInternal(recipient);
        }

        private Task DeleteSentRequestInternal(DotYouIdentity recipient)
        {
            _sentRequestValueStorage.Delete(recipient.ToByteArrayId(), _sentPrefix);
            return Task.CompletedTask;
        }

        public async Task AcceptConnectionRequest(DotYouIdentity sender, IEnumerable<ByteArrayId> circleIds)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var request = await GetPendingRequest(sender);

            if (null == request)
            {
                throw new InvalidOperationException($"No pending request was found from sender [{sender}]");
            }

            request.Validate();

            _logger.LogInformation($"Accept Connection request called for sender {request.SenderDotYouId} to {request.Recipient}");
            var remoteClientAccessToken = this.DecryptRequestExchangeCredentials(request.RSAEncryptedExchangeCredentials);

            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var (accessRegistration, clientAccessTokenReply) = await _exchangeGrantService.CreateClientAccessToken(keyStoreKey,ClientTokenType.Other);

            ConnectionRequestReply acceptedReq = new()
            {
                SenderDotYouId = _tenantContext.HostDotYouId,
                SharedSecretEncryptedCredentials = EncryptReplyExchangeCredentials(clientAccessTokenReply, remoteClientAccessToken.SharedSecret)
            };

            //TODO: XXX - no need to do RSA encryption here since we have the remoteClientAccessToken.SharedSecret
            var json = DotYouSystemSerializer.Serialize(acceptedReq);
            var payloadBytes = await _rsaPublicKeyService.EncryptPayloadForRecipient(request.SenderDotYouId, json.ToUtf8ByteArray());
            var response = await _dotYouHttpClientFactory.CreateClient<ICircleNetworkRequestHttpClient>((DotYouIdentity)request.SenderDotYouId).EstablishConnection(payloadBytes);

            if (response.Content is { Success: false } || response.IsSuccessStatusCode == false)
            {
                //public key might be invalid, destroy the cache item
                await _rsaPublicKeyService.InvalidatePublicKey((DotYouIdentity)request.SenderDotYouId);

                payloadBytes = await _rsaPublicKeyService.EncryptPayloadForRecipient(request.SenderDotYouId, json.ToUtf8ByteArray());
                response = await _dotYouHttpClientFactory.CreateClient<ICircleNetworkRequestHttpClient>((DotYouIdentity)request.SenderDotYouId).EstablishConnection(payloadBytes);

                //round 2, fail all together
                if (response.Content is { Success: false } || response.IsSuccessStatusCode == false)
                {
                    throw new Exception(
                        $"Failed to establish connection request.  Endpoint Server returned status code {response.StatusCode}.  Either response was empty or server returned a failure");
                }
            }

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var accessGrant = new AccessExchangeGrant()
            {
                //TODO: encrypting the key store key here is wierd.  this should be done in the exchange grant service
                MasterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(ref masterKey, ref keyStoreKey),
                IsRevoked = false,
                CircleGrants = await _cns.CreateCircleGrantList(circleIds?.ToList() ?? new List<ByteArrayId>(), keyStoreKey),
                AccessRegistration = accessRegistration
            };
            keyStoreKey.Wipe();

            await _cns.Connect(request.SenderDotYouId, accessGrant, remoteClientAccessToken);

            remoteClientAccessToken.AccessTokenHalfKey.Wipe();
            remoteClientAccessToken.SharedSecret.Wipe();

            await this.DeletePendingRequest((DotYouIdentity)request.SenderDotYouId);
            await this.DeleteSentRequest((DotYouIdentity)request.SenderDotYouId);
        }

        public async Task EstablishConnection(ConnectionRequestReply handshakeResponse)
        {
            // Note:
            // this method runs under the Transit Context because it's called by another identity
            // therefore, all operations that require master key or owner access must have already been completed

            //TODO: need to add a blacklist and other checks to see if we want to accept the request from the incoming DI

            var originalRequest = await this.GetSentRequestInternal((DotYouIdentity)handshakeResponse.SenderDotYouId);

            //Assert that I previously sent a request to the dotIdentity attempting to connected with me
            if (null == originalRequest)
            {
                throw new InvalidOperationException("The original request no longer exists in Sent Requests");
            }

            var accessExchangeGrant = originalRequest.PendingAccessExchangeGrant;


            //TODO: need to decrypt this AccessKeyStoreKeyEncryptedSharedSecret
            var sharedSecret = accessExchangeGrant.AccessRegistration.AccessKeyStoreKeyEncryptedSharedSecret;
            var remoteClientAccessToken = this.DecryptReplyExchangeCredentials(handshakeResponse.SharedSecretEncryptedCredentials, sharedSecret);

            await _cns.Connect(handshakeResponse.SenderDotYouId, originalRequest.PendingAccessExchangeGrant, remoteClientAccessToken);

            await this.DeleteSentRequestInternal((DotYouIdentity)originalRequest.Recipient);
            await this.DeletePendingRequestInternal((DotYouIdentity)originalRequest.Recipient);

            await _mediator.Publish(new ConnectionRequestAccepted()
            {
                Sender = (DotYouIdentity)originalRequest.SenderDotYouId
            });
        }

        public Task DeletePendingRequest(DotYouIdentity sender)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            return DeletePendingRequestInternal(sender);
        }

        private Task DeletePendingRequestInternal(DotYouIdentity sender)
        {
            _pendingRequestValueStorage.Delete(sender.ToByteArrayId(), _pendingPrefix);
            return Task.CompletedTask;
        }

        private async Task<ConnectionRequest> GetSentRequestInternal(DotYouIdentity recipient)
        {
            var result = _sentRequestValueStorage.Get<ConnectionRequest>(recipient.ToByteArrayId(), _sentPrefix);
            return result;
        }

        private string EncryptReplyExchangeCredentials(ClientAccessToken clientAccessToken, SensitiveByteArray encryptionKey)
        {
            var combinedBytes = ByteArrayUtil.Combine(clientAccessToken.Id.ToByteArray(), clientAccessToken.AccessTokenHalfKey.GetKey(), clientAccessToken.SharedSecret.GetKey());

            //TODO: encrypt using encryptionKey

            var data = Convert.ToBase64String(combinedBytes);

            combinedBytes.ToSensitiveByteArray().Wipe();

            return data;
        }

        private ClientAccessToken DecryptReplyExchangeCredentials(string replyData, SymmetricKeyEncryptedAes encryptionKey)
        {
            var combinedBytes = Convert.FromBase64String(replyData);

            //TODO: AES decrypt
            var (remoteAccessRegistrationId, remoteGrantKey, sharedSecret) = ByteArrayUtil.Split(combinedBytes, 16, 16, 16);

            return new ClientAccessToken()
            {
                Id = new Guid(remoteAccessRegistrationId),
                AccessTokenHalfKey = remoteGrantKey.ToSensitiveByteArray(),
                SharedSecret = sharedSecret.ToSensitiveByteArray(),
                ClientTokenType = ClientTokenType.Other
            };
        }

        private string EncryptRequestExchangeCredentials(DotYouIdentity recipient, ClientAccessToken clientAccessToken)
        {
            var combinedBytes = ByteArrayUtil.Combine(clientAccessToken.Id.ToByteArray(), clientAccessToken.AccessTokenHalfKey.GetKey(), clientAccessToken.SharedSecret.GetKey());

            //TODO: Now RSA Encrypt

            var data = Convert.ToBase64String(combinedBytes);
            combinedBytes.ToSensitiveByteArray().Wipe();

            return data;
        }

        private ClientAccessToken DecryptRequestExchangeCredentials(string rsaEncryptedCredentials)
        {
            //TODO look up private key from ??
            var combinedBytes = Convert.FromBase64String(rsaEncryptedCredentials);

            //TODO: rsa decrypt
            var (remoteAccessRegistrationIdBytes, remoteGrantKey, sharedSecret) = ByteArrayUtil.Split(combinedBytes, 16, 16, 16);
            return new ClientAccessToken()
            {
                Id = new Guid(remoteAccessRegistrationIdBytes),
                AccessTokenHalfKey = remoteGrantKey.ToSensitiveByteArray(),
                SharedSecret = sharedSecret.ToSensitiveByteArray(),
                ClientTokenType = ClientTokenType.Other
            };
        }
    }
}