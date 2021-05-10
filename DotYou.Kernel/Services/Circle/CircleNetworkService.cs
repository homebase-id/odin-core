using DotYou.Kernel.Storage;
using DotYou.Types;
using DotYou.Types.Certificate;
using DotYou.Types.Circle;
using Identity.Web.Services.Contacts;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Services.Identity;

namespace DotYou.Kernel.Services.Circle
{

    //Need to consider using the recpient public key instead of the dotyouid
    //meaning i can go to frodo site, click connect and the public ke cert has all i need to
    //make the connect-request as well as encrypt the request.

    //see: DotYouClaimTypes.PublicKeyCertificate

    //can I get SAMs public key certificate from the request of the original client cert auth

    public class CircleNetworkService : DotYouServiceBase, ICircleNetworkService
    {
        const string PENDING_CONNECTION_REQUESTS = "ConnectionRequests";
        const string SENT_CONNECTION_REQUESTS = "SentConnectionRequests";

        private readonly IContactService _contactService;

        public CircleNetworkService(DotYouContext context, IContactService contactService, ILogger<CircleNetworkService> logger) : base(context, logger)
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

        public async Task SendConnectionRequest(ConnectionRequest request)
        {
            this.Logger.LogInformation($"[{request.Sender}] is sending a request to the server of  [{request.Recipient}]");
            await base.HttpProxy.PostJson<ConnectionRequest>(request.Recipient, "/api/incoming/invitations/connect", request);

            WithTenantStorage<ConnectionRequest>(SENT_CONNECTION_REQUESTS, s => s.Save(request));
        }

        public Task ReceiveConnectionRequest(ConnectionRequest request)
        {
            //note: this would occur during the operation verification process
            request.Validate();

            this.Logger.LogInformation($"[{request.Recipient}] is receiving a connection request from [{request.Sender}]");

            WithTenantStorage<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.Save(request));

            /*
            try
            {
                //Note: since we're hosting both frodo and sam on the same server
                //we MUST specify the user, otherwise all people connected to this server 
                //will get the notification

                //the ci.RecpientIdentifier must also be authenticated
                _notificationHub.Clients.User(_tenantContext.Identifier).NotifyOfConnectionRequest(request);
            }
            catch (Exception)
            {
                //_logger.LogWarning("Failed to send notification to clients");
            }
             */
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
            if(null == originalRequest)
            {
                throw new InvalidOperationException("The original request no longer exists in Sent Requests");
            }

            //TODO: this is a strange way 
            DomainCertificate cert = new DomainCertificate(request.SenderPublicKeyCertificate);
            var ec = await _contactService.GetByDomainName(cert.DotYouId);

            //TODO: address how this contact merge should really happen
            var contact = new Contact()
            {
                GivenName = request.RecipientGivenName,
                Surname = request.RecipientSurname,
                DotYouId = (DotYouIdentity)cert.DotYouId,
                SystemCircle = SystemCircle.Connected,
                PrimaryEmail = null == ec ? "" : ec.PrimaryEmail,
                PublicKeyCertificate = request.SenderPublicKeyCertificate, //using Sender here because it will be the original person to which I sent the request.
                Tag = null == ec ? "" : ec.Tag
            };

            await _contactService.Save(contact);

            //await this.DeleteSentRequest(request.ConnectionRequestId);
        }

        public async Task AcceptConnectionRequest(Guid id)
        {

            var request = await GetPendingRequest(id);
            if (null == request)
            {
                throw new InvalidOperationException($"No pending request was found with id [{id}]");
            }

            request.Validate();

            this.Logger.LogInformation($"Accept Connection request called for sender {request.Sender} to {request.Recipient}");

            var cert = new DomainCertificate(request.SenderPublicKeyCertificate);
            var ec = await _contactService.GetByDomainName(cert.DotYouId);

            //TODO: add relationshipId for future analysis

            //TODO: address how this contact merge should really happen
            var contact = new Contact()
            {
                GivenName = request.SenderGivenName,
                Surname = request.SenderSurname,
                DotYouId = (DotYouIdentity)cert.DotYouId,
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

            var response = await this.HttpProxy.PostJson(request.Sender, "api/incoming/invitations/establishconnection", acceptedReq);
            
            if(!response)
            {
                //TODO: add more info
                throw new Exception("Failed to establish connection request");
            }

            await this.DeletePendingRequest(request.Id);
        }
    }
}
