using DotYou.Kernel.Storage;
using DotYou.Types;
using DotYou.Types.TrustNetwork;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace DotYou.Kernel.Services.TrustNetwork
{
    public class TrustNetworkService : DotYouServiceBase, ITrustNetworkService
    {
        const string INCOMING_CONNECTION_REQUESTS = "ConnectionRequests";
        const string SENT_CONNECTION_REQUESTS = "SentConnectionRequests";

        public TrustNetworkService(DotYouContext context, ILogger<TrustNetworkService> logger) : base(context, logger) { }

        public Task<PagedResult<ConnectionRequest>> GetPendingRequests(PageOptions pageOptions)
        {
            throw new System.NotImplementedException();
        }

        public Task<PagedResult<ConnectionRequest>> GetSentRequests(PageOptions pageRequest)
        {
            throw new System.NotImplementedException();
        }

        public async Task SendConnectionRequest(ConnectionRequest request)
        {
            await base.HttpProxy.PostJson<ConnectionRequest>(request.Recipient, "/api/incoming/invitations/connect", request);

            WithTenantStorage<ConnectionRequest>(SENT_CONNECTION_REQUESTS, s => s.Save(request));
        }

        public Task ReceiveConnectionRequest(ConnectionRequest request)
        {
            this.Logger.LogInformation($"receiving connection requst from [{request.Sender}]");

            WithTenantStorage<ConnectionRequest>(INCOMING_CONNECTION_REQUESTS, s => s.Save(request));

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
            var result = await WithTenantStorageReturn<ConnectionRequest>(INCOMING_CONNECTION_REQUESTS, s => s.Get(id));
            return result;
        }

        public Task DeletePendingRequest(Guid id)
        {
            WithTenantStorage<ConnectionRequest>(INCOMING_CONNECTION_REQUESTS, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public Task EstablishConnection(ConnectionRequest request)
        {
            throw new NotImplementedException();
        }

        public Task AcceptConnectionRequest(Guid requestId)
        {
            throw new NotImplementedException();
        }

        public Task<ConnectionRequest> GetSentRequest(Guid id)
        {
            throw new NotImplementedException();
        }
    }
}
