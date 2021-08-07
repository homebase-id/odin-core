using DotYou.Types;
using DotYou.Types.Circle;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Linq.Expressions;
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

        private readonly IPersonService _personService;

        public CircleNetworkService(DotYouContext context, IPersonService personService, ILogger<CircleNetworkService> logger, IHubContext<NotificationHub, INotificationHub> hub, DotYouHttpClientFactory fac) : base(context, logger, hub, fac)
        {
            _personService = personService;
        }

        public async Task<PagedResult<ConnectionRequest>> GetPendingRequests(PageOptions pageOptions)
        {
            Expression<Func<ConnectionRequest, string>> sortKeySelector = key => key.SenderGivenName;
            Expression<Func<ConnectionRequest, bool>> predicate = c => true;  //HACK: need to update the storage provider GetList method
            var results = await WithTenantStorageReturnList<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.Find(predicate, ListSortDirection.Ascending, sortKeySelector, pageOptions));

            // var results = await WithTenantStorageReturnList<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, storage => storage.GetList(pageOptions));
            return results;
        }

        public async Task<PagedResult<ConnectionRequest>> GetSentRequests(PageOptions pageOptions)
        {
            var results = await WithTenantStorageReturnList<ConnectionRequest>(SENT_CONNECTION_REQUESTS, storage => storage.GetList(pageOptions));
            return results;
        }

        public async Task SendConnectionRequest(ConnectionRequestHeader header)
        {
            Guard.Argument(header, nameof(header)).NotNull();
            Guard.Argument((string) header.Recipient, nameof(header.Recipient)).NotNull();
            Guard.Argument(header.Id, nameof(header.Id)).HasValue();

            var request = new ConnectionRequest();
            request.Id = header.Id;
            request.Recipient = header.Recipient;
            request.Message = header.Message;

            request.SenderDotYouId = this.Context.HostDotYouId;
            request.ReceivedTimestampMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            //TODO: these need to pull from the identity attribute server using the public profile attributes
            request.SenderGivenName = this.Context.TenantCertificate.OwnerName.GivenName;
            request.SenderSurname = this.Context.TenantCertificate.OwnerName.Surname;

            this.Logger.LogInformation($"[{request.SenderDotYouId}] is sending a request to the server of  [{request.Recipient}]");

            var response = await base.CreatePerimeterHttpClient(request.Recipient).DeliverConnectionRequest(request);

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

        public async Task<Profile> GetProfile(DotYouIdentity dotYouId)
        {
            Guard.Argument(dotYouId.Id, nameof(dotYouId)).NotNull().NotEmpty();

            var response = await base.CreatePerimeterHttpClient(dotYouId).GetProfile();

            //TODO: this needs to check many more things - ie. : is the endpoint a DotYou server, is their profile configured, etc
            //for #prototrial, i will simply return a 404 if we don't get a success status
            if (!response.IsSuccessStatusCode)
            {
                return null;
                // //TODO: add more info
                // throw new Exception("Failed to establish connection request");
            }

            return response.Content;
        }

        public async Task<SystemCircle> GetSystemCircle(DotYouIdentity dotYouId)
        {
            var contact = await _personService.GetByDotYouId(dotYouId);

            if (contact == null)
            {
                //TODO: I wonder if we should throw an exception because this is inaccurate
                return SystemCircle.PublicAnonymous;
            }

            return contact.SystemCircle;
        }

        public Task<bool> Disconnect(DotYouIdentity dotYouId)
        {
            throw new NotImplementedException();
        }

        public Task<bool> Block(DotYouIdentity dotYouId)
        {
            throw new NotImplementedException();
        }
        
        public Task<bool> Unblock(DotYouIdentity dotYouId)
        {
            throw new NotImplementedException();
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

            DomainCertificate cert = new DomainCertificate(request.SenderPublicKeyCertificate);
            var ec = await _personService.GetByDotYouId(cert.DotYouId);

            //TODO: address how this contact merge should really happen
            //TODO: add relationship id to unify the connection; perhaps use ConnectionRequestId

            //create a new contact
            var person = new Person()
            {
                Id = ec?.Id ?? Guid.NewGuid(),
                GivenName = request.RecipientGivenName,
                Surname = request.RecipientSurname,
                DotYouId = cert.DotYouId,
                SystemCircle = SystemCircle.Connected,
                PrimaryEmail = ec == null ? "" : ec.PrimaryEmail,
                PublicKeyCertificate = request.SenderPublicKeyCertificate, //using Sender here because it will be the original person to which I sent the request.
                Tag = ec == null ? "" : ec.Tag
            };

            await _personService.Save(person);

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
            var ec = await _personService.GetByDotYouId(cert.DotYouId);

            //TODO: add relationshipId for future analysis

            //TODO: address how this contact merge should really happen

            var contact = new Person()
            {
                Id = ec?.Id ?? Guid.NewGuid(),
                GivenName = request.SenderGivenName,
                Surname = request.SenderSurname,
                DotYouId = cert.DotYouId,
                SystemCircle = SystemCircle.Connected,
                PrimaryEmail = ec == null ? "" : ec.PrimaryEmail,
                PublicKeyCertificate = request.SenderPublicKeyCertificate, //using Sender here because it will be the original person to which I sent the request.
                Tag = ec == null ? "" : ec.Tag
            };

            await _personService.Save(contact);

            //call to request.Sender's agent to establish connection.
            EstablishConnectionRequest acceptedReq = new()
            {
                ConnectionRequestId = id,
                RecipientGivenName = this.Context.TenantCertificate.OwnerName.GivenName,
                RecipientSurname = this.Context.TenantCertificate.OwnerName.Surname
            };

            var response = await this.CreatePerimeterHttpClient(request.SenderDotYouId).EstablishConnection(acceptedReq);

            if (!response.IsSuccessStatusCode || response.Content is not {Success: true})
            {
                //TODO: add more info and clarify
                throw new Exception($"Failed to establish connection request.  Endpoint Server returned status code {response.StatusCode}.  Either response was empty or server returned a failure");
            }

            await this.DeletePendingRequest(request.Id);
        }
    }
}