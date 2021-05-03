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

        public TrustNetworkService(DotYouContext context, ILogger<TrustNetworkService> logger) : base(context, logger)
        {
        }

        public Task<PagedResult<ConnectionRequest>> GetPendingRequests(PageOptions pageOptions)
        {
            throw new System.NotImplementedException();
        }

        public Task<PagedResult<ConnectionRequest>> GetSentRequests()
        {
            throw new System.NotImplementedException();
        }

        public async Task SendConnectionRequest(ConnectionRequest request)
        {
            await base.HttpProxy.Post<ConnectionRequest>("/api/incoming/invivitations/connect", request);
        }

        public Task ReceiveConnectionRequest(ConnectionRequest request)
        {
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

        public Task<ConnectionRequest> GetPendingRequest(Guid id)
        {
            var result = WithTenantStorageReturn<ConnectionRequest>(INCOMING_CONNECTION_REQUESTS, s => s.Get(id));
            return result;
        }

        public Task DeletePendingRequest(Guid invitationId)
        {
            throw new NotImplementedException();
        }

    }
}
