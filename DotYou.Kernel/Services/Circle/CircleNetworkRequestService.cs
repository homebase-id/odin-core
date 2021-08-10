using System;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Dawn;
using DotYou.IdentityRegistry;
using DotYou.Kernel.HttpClient;
using DotYou.Kernel.Services.Identity;
using DotYou.Kernel.Services.Owner.Data;
using DotYou.Types;
using DotYou.Types.Circle;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Circle
{
    public class CircleNetworkRequestService : DotYouServiceBase, ICircleNetworkRequestService
    {
        const string PENDING_CONNECTION_REQUESTS = "ConnectionRequests";
        const string SENT_CONNECTION_REQUESTS = "SentConnectionRequests";

        private readonly ICircleNetworkService _cns;
        private readonly IOwnerDataAttributeManagementService _mgts;

        public CircleNetworkRequestService(DotYouContext context, ICircleNetworkService cns, ILogger<CircleNetworkService> logger, IHubContext<NotificationHub, INotificationHub> hub, DotYouHttpClientFactory fac, IOwnerDataAttributeManagementService mgts) : base(context, logger, hub, fac)
        {
            _cns = cns;
            _mgts = mgts;
        }

        public async Task<PagedResult<ConnectionRequest>> GetPendingRequests(PageOptions pageOptions)
        {
            Expression<Func<ConnectionRequest, string>> sortKeySelector = key => key.Name.Personal;
            Expression<Func<ConnectionRequest, bool>> predicate = c => true; //HACK: need to update the storage provider GetList method
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

            var primaryName = await _mgts.GetPrimaryName();
            request.Name = primaryName;

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
            
            await _cns.Connect(request.SenderPublicKeyCertificate, request.Name);
            
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

            await _cns.Connect(request.SenderPublicKeyCertificate, request.Name);

            //Now send back an acknowledgement

            var primaryName = await _mgts.GetPrimaryName();
            EstablishConnectionRequest acceptedReq = new()
            {
                ConnectionRequestId = id,
                Name = primaryName
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