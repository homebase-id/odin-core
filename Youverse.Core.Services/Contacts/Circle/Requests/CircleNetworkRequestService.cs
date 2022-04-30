using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using MediatR.Pipeline;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.Exchange;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Mediator.ClientNotifications;
using Youverse.Core.Services.Profile;

namespace Youverse.Core.Services.Contacts.Circle.Requests
{
    public class CircleNetworkRequestService : ICircleNetworkRequestService
    {
        const string PENDING_CONNECTION_REQUESTS = "ConnectionRequests";
        const string SENT_CONNECTION_REQUESTS = "SentConnectionRequests";

        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ICircleNetworkService _cns;
        private readonly ILogger<ICircleNetworkRequestService> _logger;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly IProfileAttributeManagementService _mgts;
        private readonly ISystemStorage _systemStorage;
        private readonly IAppRegistrationService _appReg;
        private readonly IMediator _mediator;
        private readonly TenantContext _tenantContext;
        private readonly IPublicKeyService _pkService;

        private readonly IDriveService _driveService;
        private readonly ExchangeGrantService _exchangeGrantService;

        public CircleNetworkRequestService(IAppRegistrationService appReg, DotYouContextAccessor contextAccessor, ICircleNetworkService cns, ILogger<ICircleNetworkRequestService> logger, IDotYouHttpClientFactory dotYouHttpClientFactory, IProfileAttributeManagementService mgts, ISystemStorage systemStorage, IMediator mediator, TenantContext tenantContext, IPublicKeyService pkService, ExchangeGrantService exchangeGrantService, IDriveService driveService)
        {
            _contextAccessor = contextAccessor;
            _cns = cns;
            _logger = logger;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _mgts = mgts;
            _systemStorage = systemStorage;
            _mediator = mediator;
            _tenantContext = tenantContext;
            _pkService = pkService;
            _exchangeGrantService = exchangeGrantService;
            _driveService = driveService;
            _contextAccessor = contextAccessor;
            _appReg = appReg;

            _systemStorage.WithTenantSystemStorage<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.EnsureIndex(cr => cr.SenderDotYouId, true));
            _systemStorage.WithTenantSystemStorage<ConnectionRequestHeader>(SENT_CONNECTION_REQUESTS, s => s.EnsureIndex(cr => cr.Recipient, true));
        }

        public async Task<PagedResult<ConnectionRequest>> GetPendingRequests(PageOptions pageOptions)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            Expression<Func<ConnectionRequest, string>> sortKeySelector = key => key.Name.Personal;
            Expression<Func<ConnectionRequest, bool>> predicate = c => true; //HACK: need to update the storage provider GetList method
            var results = await _systemStorage.WithTenantSystemStorageReturnList<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.Find(predicate, ListSortDirection.Ascending, sortKeySelector, pageOptions));

            return results;
        }

        public async Task<PagedResult<ConnectionRequest>> GetSentRequests(PageOptions pageOptions)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var results = await _systemStorage.WithTenantSystemStorageReturnList<ConnectionRequest>(SENT_CONNECTION_REQUESTS, storage => storage.GetList(pageOptions));
            return results;
        }

        public async Task SendConnectionRequest(ConnectionRequestHeader header)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            Guard.Argument(header, nameof(header)).NotNull();
            Guard.Argument((string) header.Recipient, nameof(header.Recipient)).NotNull();
            Guard.Argument(header.Id, nameof(header.Id)).HasValue();

            var remoteRsaKey = await _pkService.GetOnlinePublicKey();

            //HACK: get list of drives from the header (requires UI updates)
            var drives = await _driveService.GetDrives(PageOptions.All);
            var (accessRegistration, clientAccessToken) = await _exchangeGrantService.RegisterExchangeGrant(drives.Results.ToList());

            //TODO: need to encrypt the message as well as the rsa credentials
            var request = new ConnectionRequest
            {
                Id = header.Id,
                Recipient = header.Recipient,
                Message = header.Message,
                SenderDotYouId = this._tenantContext.HostDotYouId, //this should not be required since it's set on the receiving end
                ReceivedTimestampMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), //this should not be required since it's set on the receiving end
                RSAEncryptedExchangeCredentials = EncryptRequestExchangeCredentials(header.Recipient, clientAccessToken)
            };

            var profile = await _mgts.GetBasicConnectedProfile(fallbackToEmpty: true);

            //TODO removed so I can test sending friend requests
            //Guard.Argument(profile, nameof(profile)).NotNull("The DI owner's primary name is not correctly configured");
            //Guard.Argument(profile.Name.Personal, nameof(profile.Name.Personal)).NotNull("The DI owner's primary name is not correctly configured");
            //Guard.Argument(profile.Name.Surname, nameof(profile.Name.Surname)).NotNull("The DI owner's primary name is not correctly configured");

            request.Name = profile.Name;
            _logger.LogInformation($"[{request.SenderDotYouId}] is sending a request to the server of [{request.Recipient}]");

            var response = await _dotYouHttpClientFactory.CreateClient(request.Recipient).DeliverConnectionRequest(request);

            if (response.Content is {Success: false} || response.IsSuccessStatusCode == false)
            {
                //TODO: add more info
                throw new Exception("Failed to establish connection request");
            }

            clientAccessToken.SharedSecret.Wipe();
            clientAccessToken.AccessTokenHalfKey.Wipe();

            //Note: the pending access reg id attached only AFTER we send the request
            request.RSAEncryptedExchangeCredentials = "";
            request.PendingAccessRegistrationId = accessRegistration.Id;
            _systemStorage.WithTenantSystemStorage<ConnectionRequest>(SENT_CONNECTION_REQUESTS, s => s.Save(request));
        }

        //TODO: this needs to be moved to a transit-specific service
        public Task ReceiveConnectionRequest(ConnectionRequest request)
        {
            //in transit context on the recipient's server, i need to receive a request
            // - there is no app
            // - the caller is not an owner and does not have a master key

            //HACK - need to figure out how to secure receiving of connection requests from other DIs; this might be robot detection code + the fact they're in the youverse network
            //_context.GetCurrent()/AssertCanManageConnections();

            //TODO: check robot detection code

            //note: this would occur during the operation verification process
            request.Validate();
            _logger.LogInformation($"[{request.Recipient}] is receiving a connection request from [{request.SenderDotYouId}]");
            _systemStorage.WithTenantSystemStorage<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.Save(request));

            _mediator.Publish(new ConnectionRequestReceived()
            {
                Sender = request.SenderDotYouId
            });

            return Task.CompletedTask;
        }

        public async Task<ConnectionRequest> GetPendingRequest(DotYouIdentity sender)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.FindOne(c => c.SenderDotYouId == sender));
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
            _systemStorage.WithTenantSystemStorage<ConnectionRequest>(SENT_CONNECTION_REQUESTS, s => s.Delete(recipient));
            _systemStorage.WithTenantSystemStorage<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.DeleteMany(cr => cr.SenderDotYouId == recipient));
            return Task.CompletedTask;
        }

        public async Task AcceptConnectionRequest(DotYouIdentity sender)
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

            //HACK: USE ALL DRIVES FOR THE MOMENT, will need to get a list from the UI
            var drives = await _driveService.GetDrives(PageOptions.All);
            var (accessRegistration, clientAccessTokenReply) = await _exchangeGrantService.RegisterExchangeGrant(drives.Results.ToList());

            //Send an acknowledgement by establishing a connection
            var p = await _mgts.GetBasicConnectedProfile(fallbackToEmpty: true);

            ConnectionRequestReply acceptedReq = new()
            {
                Name = p.Name,
                ProfilePic = p.Photo,
                SharedSecretEncryptedCredentials = EncryptReplyExchangeCredentials(clientAccessTokenReply, remoteClientAccessToken.SharedSecret)
            };

            var response = await _dotYouHttpClientFactory.CreateClient(request.SenderDotYouId).EstablishConnection(acceptedReq);

            if (!response.IsSuccessStatusCode || response.Content is not {Success: true})
            {
                throw new Exception($"Failed to establish connection request.  Endpoint Server returned status code {response.StatusCode}.  Either response was empty or server returned a failure");
            }

            await _cns.Connect(request.SenderDotYouId, request.Name, accessRegistration.Id, remoteClientAccessToken);

            remoteClientAccessToken.AccessTokenHalfKey.Wipe();
            remoteClientAccessToken.SharedSecret.Wipe();

            await this.DeletePendingRequest(request.SenderDotYouId);
            await this.DeleteSentRequest(request.SenderDotYouId);
        }

        public async Task EstablishConnection(ConnectionRequestReply handshakeResponse)
        {
            //TODO: need to add a blacklist and other checks to see if we want to accept the request from the incoming DI

            var originalRequest = await this.GetSentRequestInternal(handshakeResponse.SenderDotYouId);

            //Assert that I previously sent a request to the dotIdentity attempting to connected with me
            if (null == originalRequest)
            {
                throw new InvalidOperationException("The original request no longer exists in Sent Requests");
            }

            var accessReg = await _exchangeGrantService.GetAccessRegistration(originalRequest.PendingAccessRegistrationId);

            //TODO: need to decrypt this AccessKeyStoreKeyEncryptedSharedSecret
            var sharedSecret = accessReg.AccessKeyStoreKeyEncryptedSharedSecret;

            var remoteClientAccessToken = this.DecryptReplyExchangeCredentials(handshakeResponse.SharedSecretEncryptedCredentials, sharedSecret);

            await _cns.Connect(handshakeResponse.SenderDotYouId, handshakeResponse.Name, originalRequest.PendingAccessRegistrationId, remoteClientAccessToken);

            await this.DeleteSentRequestInternal(originalRequest.Recipient);

            //just in case I the recipient also sent me a request (this shouldn't happen but #prototrial has no constructs to stop this other than UI)
            await this.DeletePendingRequestInternal(originalRequest.Recipient);

            await _mediator.Publish(new ConnectionRequestAccepted()
            {
                Sender = originalRequest.SenderDotYouId
            });
        }

        public Task DeletePendingRequest(DotYouIdentity sender)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            return DeletePendingRequestInternal(sender);
        }

        private Task DeletePendingRequestInternal(DotYouIdentity sender)
        {
            _systemStorage.WithTenantSystemStorage<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.DeleteMany(cr => cr.SenderDotYouId == sender));

            //this shouldn't happen but #prototrial has no constructs to stop this other than UI)
            _systemStorage.WithTenantSystemStorage<ConnectionRequest>(SENT_CONNECTION_REQUESTS, s => s.Delete(sender));

            return Task.CompletedTask;
        }

        private async Task<ConnectionRequest> GetSentRequestInternal(DotYouIdentity recipient)
        {
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<ConnectionRequest>(SENT_CONNECTION_REQUESTS, s => s.Get(recipient));
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
                SharedSecret = sharedSecret.ToSensitiveByteArray()
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
                SharedSecret = sharedSecret.ToSensitiveByteArray()
            };
        }
    }
}