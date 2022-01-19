using System;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Notifications;
using Youverse.Core.Services.Profile;

namespace Youverse.Core.Services.Contacts.Circle
{
    public class CircleNetworkRequestService : ICircleNetworkRequestService
    {
        const string PENDING_CONNECTION_REQUESTS = "ConnectionRequests";
        const string SENT_CONNECTION_REQUESTS = "SentConnectionRequests";

        private readonly DotYouContext _context;
        private readonly ICircleNetworkService _cns;
        private readonly ILogger<ICircleNetworkRequestService> _logger;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly IProfileAttributeManagementService _mgts;
        private readonly ISystemStorage _systemStorage;

        public CircleNetworkRequestService(DotYouContext context, ICircleNetworkService cns, ILogger<ICircleNetworkRequestService> logger, AppNotificationHandler hub, IDotYouHttpClientFactory dotYouHttpClientFactory, IProfileAttributeManagementService mgts, ISystemStorage systemStorage)
        {
            _context = context;
            _cns = cns;
            _logger = logger;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _mgts = mgts;
            _systemStorage = systemStorage;
            _context = context;
            _systemStorage.WithTenantSystemStorage<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.EnsureIndex(cr => cr.SenderDotYouId, true));
            _systemStorage.WithTenantSystemStorage<ConnectionRequestHeader>(SENT_CONNECTION_REQUESTS, s => s.EnsureIndex(cr => cr.Recipient, true));
        }

        public async Task<PagedResult<ConnectionRequest>> GetPendingRequests(PageOptions pageOptions)
        {
            _context.AssertCanManageConnections();
            
            Expression<Func<ConnectionRequest, string>> sortKeySelector = key => key.Name.Personal;
            Expression<Func<ConnectionRequest, bool>> predicate = c => true; //HACK: need to update the storage provider GetList method
            var results = await _systemStorage.WithTenantSystemStorageReturnList<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.Find(predicate, ListSortDirection.Ascending, sortKeySelector, pageOptions));

            return results;
        }

        public async Task<PagedResult<ConnectionRequest>> GetSentRequests(PageOptions pageOptions)
        {
            _context.AssertCanManageConnections();
            
            var results = await _systemStorage.WithTenantSystemStorageReturnList<ConnectionRequest>(SENT_CONNECTION_REQUESTS, storage => storage.GetList(pageOptions));
            return results;
        }

        public async Task SendConnectionRequest(ConnectionRequestHeader header)
        {
            _context.AssertCanManageConnections();
            
            Guard.Argument(header, nameof(header)).NotNull();
            Guard.Argument((string) header.Recipient, nameof(header.Recipient)).NotNull();
            Guard.Argument(header.Id, nameof(header.Id)).HasValue();

            var request = new ConnectionRequest
            {
                Id = header.Id,
                Recipient = header.Recipient,
                Message = header.Message,
                SenderDotYouId = this._context.HostDotYouId, //this should not be required since it's set on the receiving end
                ReceivedTimestampMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() //this should not be required since it's set on the receiving end
            };

            var profile = await _mgts.GetBasicConnectedProfile(fallbackToEmpty: true);

            //TODO removed so I can test sending friend requests
            //Guard.Argument(profile, nameof(profile)).NotNull("The DI owner's primary name is not correctly configured");
            //Guard.Argument(profile.Name.Personal, nameof(profile.Name.Personal)).NotNull("The DI owner's primary name is not correctly configured");
            //Guard.Argument(profile.Name.Surname, nameof(profile.Name.Surname)).NotNull("The DI owner's primary name is not correctly configured");

            request.Name = profile.Name;
            _logger.LogInformation($"[{request.SenderDotYouId}] is sending a request to the server of [{request.Recipient}]");

            var response = await _dotYouHttpClientFactory.CreateClient(request.Recipient).DeliverConnectionRequest(request);

            if (response.Content is {Success: false})
            {
                //TODO: add more info
                throw new Exception("Failed to establish connection request");
            }

            _systemStorage.WithTenantSystemStorage<ConnectionRequest>(SENT_CONNECTION_REQUESTS, s => s.Save(request));
        }

        //TODO: this needs to be moved to a transit-specific service
        public Task ReceiveConnectionRequest(ConnectionRequest request)
        {
            _context.AssertCanManageConnections();
            
            //note: this would occur during the operation verification process
            request.Validate();
            _logger.LogInformation($"[{request.Recipient}] is receiving a connection request from [{request.SenderDotYouId}]");
            _systemStorage.WithTenantSystemStorage<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.Save(request));

            //this.Notify.ConnectionRequestReceived(request).Wait();

            return Task.CompletedTask;
        }

        public async Task<ConnectionRequest> GetPendingRequest(DotYouIdentity sender)
        {
            _context.AssertCanManageConnections();
            
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.FindOne(c => c.SenderDotYouId == sender));
            return result;
        }

        public async Task<ConnectionRequest> GetSentRequest(DotYouIdentity recipient)
        {
            //this works in both transit and app contexts
            _context.AssertCanManageConnections();
            
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<ConnectionRequest>(SENT_CONNECTION_REQUESTS, s => s.Get(recipient));
            return result;
        }

        public Task DeleteSentRequest(DotYouIdentity recipient)
        {
            _context.AssertCanManageConnections();
            
            _systemStorage.WithTenantSystemStorage<ConnectionRequest>(SENT_CONNECTION_REQUESTS, s => s.Delete(recipient));

            //this shouldn't happen but #prototrial has no constructs to stop this other than UI)
            _systemStorage.WithTenantSystemStorage<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.DeleteMany(cr => cr.SenderDotYouId == recipient));

            return Task.CompletedTask;
        }

        public async Task EstablishConnection(AcknowledgedConnectionRequest request)
        {
            _context.AssertCanManageConnections();
            
            //grab the request that was sent by the DI that sent me this acknowledgement
            var originalRequest = await this.GetSentRequest(request.SenderDotYouId);

            //Assert that I previously sent a request to the dotIdentity attempting to connected with me
            if (null == originalRequest)
            {
                throw new InvalidOperationException("The original request no longer exists in Sent Requests");
            }

            await _cns.Connect(request.SenderDotYouId, request.Name);

            await this.DeleteSentRequest(originalRequest.Recipient);

            //just in case I the recipient also sent me a request (this shouldn't happen but #prototrial has no constructs to stop this other than UI)
            await this.DeletePendingRequest(originalRequest.Recipient);

            // this.Notify.ConnectionRequestAccepted(request).Wait();
        }

        public async Task AcceptConnectionRequest(DotYouIdentity sender)
        {
            _context.AssertCanManageConnections();
            
            var request = await GetPendingRequest(sender);
            if (null == request)
            {
                throw new InvalidOperationException($"No pending request was found from sender [{sender}]");
            }

            request.Validate();

            _logger.LogInformation($"Accept Connection request called for sender {request.SenderDotYouId} to {request.Recipient}");

            // await _cns.Connect(request.SenderPublicKeyCertificate, request.Name);
            await _cns.Connect(request.SenderDotYouId, request.Name);

            //Now send back an acknowledgement by establishing a connection

            var p = await _mgts.GetBasicConnectedProfile(fallbackToEmpty: true);

            AcknowledgedConnectionRequest acceptedReq = new()
            {
                Name = p.Name,
                ProfilePic = p.Photo
            };

            var response = await _dotYouHttpClientFactory.CreateClient(request.SenderDotYouId).EstablishConnection(acceptedReq);

            if (!response.IsSuccessStatusCode || response.Content is not {Success: true})
            {
                //TODO: add more info and clarify
                throw new Exception($"Failed to establish connection request.  Endpoint Server returned status code {response.StatusCode}.  Either response was empty or server returned a failure");
            }

            await this.DeletePendingRequest(request.SenderDotYouId);

            //Just in case I had sent a request, lets delete it too (this shouldn't happen but #prototrial has no constructs to stop this other than UI)
            await this.DeleteSentRequest(request.SenderDotYouId);
        }

        public Task DeletePendingRequest(DotYouIdentity sender)
        {
            _context.AssertCanManageConnections();
            
            _systemStorage.WithTenantSystemStorage<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.DeleteMany(cr => cr.SenderDotYouId == sender));

            //this shouldn't happen but #prototrial has no constructs to stop this other than UI)
            _systemStorage.WithTenantSystemStorage<ConnectionRequest>(SENT_CONNECTION_REQUESTS, s => s.Delete(sender));

            return Task.CompletedTask;
        }
    }
}