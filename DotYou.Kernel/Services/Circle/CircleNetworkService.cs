using DotYou.Types;
using DotYou.Types.Circle;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Dawn;
using DotYou.IdentityRegistry;
using DotYou.Kernel.HttpClient;
using DotYou.Kernel.Services.Contacts;
using DotYou.Kernel.Services.Identity;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace DotYou.Kernel.Services.Circle
{
    //Need to consider using the recipient public key instead of the dotyouid
    //meaning i can go to frodo site, click connect and the public ke cert has all i need to
    //make the connect-request as well as encrypt the request.

    //see: DotYouClaimTypes.PublicKeyCertificate

    //can I get SAMs public key certificate from the request of the original client cert auth

    public class CircleNetworkService : DotYouServiceBase, ICircleNetworkService
    {
        const string PENDING_CONNECTION_REQUESTS = "ConnectionRequests";
        const string SENT_CONNECTION_REQUESTS = "SentConnectionRequests";

        private readonly IContactService _contactService;

        public CircleNetworkService(DotYouContext context, IContactService contactService, ILogger<CircleNetworkService> logger, IHubContext<NotificationHub, INotificationHub> hub, DotYouHttpClientFactory fac) : base(context, logger, hub, fac)
        {
            _contactService = contactService;
        }

        public async Task<PagedResult<ConnectionRequest>> GetPendingRequests(PageOptions pageOptions)
        {
            var results = await WithTenantStorageReturnList<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, storage => storage.GetList(pageOptions));
            return results;
        }

        public async Task<PagedResult<ConnectionRequest>> GetSentRequests(PageOptions pageOptions)
        {
            var results = await WithTenantStorageReturnList<ConnectionRequest>(SENT_CONNECTION_REQUESTS, storage => storage.GetList(pageOptions));
            return results;
        }

        //public async Task SendConnectionRequest(ConnectionRequest request)
        public async Task SendConnectionRequest(ConnectionRequestHeader header)
        {
            Guard.Argument(header, nameof(header)).NotNull();
            Guard.Argument((string) header.Recipient, nameof(header.Recipient)).NotNull();
            Guard.Argument(header.Id, nameof(header.Id)).HasValue();

            var request = new ConnectionRequest();
            request.Id = header.Id;
            request.Recipient = header.Recipient;
            request.Message = header.Message;

            request.SenderDotYouId = this.Context.DotYouId;
            request.ReceivedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            //TODO: these need to pull from the identity attribute server using the public profile attributes
            request.SenderGivenName = this.Context.TenantCertificate.OwnerName.GivenName;
            request.SenderSurname = this.Context.TenantCertificate.OwnerName.Surname;

            this.Logger.LogInformation($"[{request.SenderDotYouId}] is sending a request to the server of  [{request.Recipient}]");

            var response = await base.CreatePerimeterHttpClient(request.Recipient).SendConnectionRequest(request);

            if (!response.Content.Success)
            {
                //TODO: add more info
                throw new Exception("Failed to establish connection request");
            }
            
            WithTenantStorage<ConnectionRequest>(SENT_CONNECTION_REQUESTS, s => s.Save(request));
        }

        public Task ReceiveConnectionRequest(ConnectionRequest request)
        {
            //note: this would occur during the operation verification process
            request.Validate();
            this.Logger.LogInformation($"[{request.Recipient}] is receiving a connection request from [{request.SenderDotYouId}]");
            WithTenantStorage<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.Save(request));

            this.Notify.ConnectionRequestReceived(request).Wait();

            return Task.CompletedTask;
        }

        public async Task<ConnectionRequest> GetPendingRequest(Guid id)
        {
            var result = await WithTenantStorageReturnSingle<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.Get(id));
            return result;
        }

        public Task DeletePendingRequest(Guid id)
        {
            WithTenantStorage<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public async Task<ConnectionRequest> GetSentRequest(Guid id)
        {
            var result = await WithTenantStorageReturnSingle<ConnectionRequest>(SENT_CONNECTION_REQUESTS, s => s.Get(id));
            return result;
        }

        public async Task EstablishConnection(EstablishConnectionRequest request)
        {
            var originalRequest = await this.GetSentRequest(request.ConnectionRequestId);

            //Assert that I previously sent a request to the dotIdentity attempting to connected with me
            if (null == originalRequest)
            {
                throw new InvalidOperationException("The original request no longer exists in Sent Requests");
            }

            //TODO: this is a strange way 
            DomainCertificate cert = new DomainCertificate(request.SenderPublicKeyCertificate);
            var ec = await _contactService.GetByDotYouId(cert.DotYouId);

            //TODO: address how this contact merge should really happen
            var contact = new Contact()
            {
                GivenName = request.RecipientGivenName,
                Surname = request.RecipientSurname,
                DotYouId = (DotYouIdentity) cert.DotYouId,
                SystemCircle = SystemCircle.Connected,
                PrimaryEmail = null == ec ? "" : ec.PrimaryEmail,
                PublicKeyCertificate = request.SenderPublicKeyCertificate, //using Sender here because it will be the original person to which I sent the request.
                Tag = null == ec ? "" : ec.Tag
            };

            await _contactService.Save(contact);

            //await this.DeleteSentRequest(request.ConnectionRequestId);

            this.Notify.ConnectionRequestAccepted(request).Wait();
        }

        public async Task AcceptConnectionRequest(Guid id)
        {
            var request = await GetPendingRequest(id);
            if (null == request)
            {
                throw new InvalidOperationException($"No pending request was found with id [{id}]");
            }

            request.Validate();

            this.Logger.LogInformation($"Accept Connection request called for sender {request.SenderDotYouId} to {request.Recipient}");

            var cert = new DomainCertificate(request.SenderPublicKeyCertificate);
            var ec = await _contactService.GetByDotYouId(cert.DotYouId);

            //TODO: add relationshipId for future analysis

            //TODO: address how this contact merge should really happen
            var contact = new Contact()
            {
                GivenName = request.SenderGivenName,
                Surname = request.SenderSurname,
                DotYouId = (DotYouIdentity) cert.DotYouId,
                SystemCircle = SystemCircle.Connected,
                PrimaryEmail = null == ec ? "" : ec.PrimaryEmail,
                PublicKeyCertificate = request.SenderPublicKeyCertificate,
                Tag = null == ec ? "" : ec.Tag
            };

            //Upsert
            await _contactService.Save(contact);

            //call to request.Sender's agent to establish connection.
            EstablishConnectionRequest acceptedReq = new()
            {
                ConnectionRequestId = id,
                RecipientGivenName = this.Context.TenantCertificate.OwnerName.GivenName,
                RecipientSurname = this.Context.TenantCertificate.OwnerName.Surname
            };

            var response = await this.CreatePerimeterHttpClient(request.SenderDotYouId).EstablishConnection(acceptedReq);

            if (!response.Content.Success)
            {
                //TODO: add more info
                throw new Exception("Failed to establish connection request");
            }

            await this.DeletePendingRequest(request.Id);
        }
    }
}