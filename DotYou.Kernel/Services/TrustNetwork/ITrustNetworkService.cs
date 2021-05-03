using DotYou.Types;
using DotYou.Types.TrustNetwork;
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
        Task<PagedResult<ConnectionRequest>> GetPendingRequests(PageOptions pageRequest);
        
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
}
