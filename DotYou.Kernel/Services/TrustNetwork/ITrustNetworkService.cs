using DotYou.Types;
using DotYou.Types.TrustNetwork;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace DotYou.Kernel.Services.TrustNetwork
{

    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public interface ITrustNetworkService
    {

        /// <summary>
        /// Sends a <see cref="ConnectionRequest"/> as an invitation.
        /// </summary>
        /// <returns></returns>
        Task SendConnectionRequest(ConnectionRequest request);


        /// <summary>
        /// Get outgoing requests awaiting approval by their recipient
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<ConnectionRequest>> GetSentRequests();


        /// <summary>
        /// Gets a list of requests awaiting approval.
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<ConnectionRequest>> GetPendingRequests(PageRequest pageRequest);
        
        /// <summary>
        /// Gets a pending <see cref="ConnectionRequest"/> by Id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<ConnectionRequest> GetPendingRequest(Guid id);

        /// <summary>
        /// Stores an incoming request.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task ReceiveConnectionRequest(ConnectionRequest request);

        /// <summary>
        /// Deletes a pending request.  This is useful if the user decides to ignore a request.
        /// </summary>
        /// <param name="requestId"></param>
        /// <returns></returns>
        Task DeletePendingRequest(Guid requestId);
    }


    public class TrustNetworkService : ITrustNetworkService
    {
        ILogger<TrustNetworkService> _logger;
        IDotYouHttpClientProxy _httpProxy;
        DotYouContext _context;

        public TrustNetworkService(DotYouContext context, ILogger<TrustNetworkService> logger, IDotYouHttpClientProxy httpProxy)
        {
            _logger = logger;
            _httpProxy = httpProxy;
            _context = context;
        }

        public Task<PagedResult<ConnectionRequest>> GetPendingRequests(PageRequest pageRequest)
        {
            throw new System.NotImplementedException();
        }

        public Task<PagedResult<ConnectionRequest>> GetSentRequests()
        {
            throw new System.NotImplementedException();
        }

        public async Task SendConnectionRequest(ConnectionRequest request)
        {
            await _httpProxy.Post<ConnectionRequest>(request.Recipient, "/api/incoming/invivitations/connect", request);
        }

        public Task ReceiveConnectionRequest(ConnectionRequest request)
        {

            return Task.CompletedTask;
        }

        public Task<ConnectionRequest> GetPendingRequest(Guid id)
        {
            throw new NotImplementedException();
        }
        public Task DeletePendingRequest(Guid invitationId)
        {
            throw new NotImplementedException();
        }
    }
}
