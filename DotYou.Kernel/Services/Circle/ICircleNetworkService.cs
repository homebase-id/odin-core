using DotYou.Types;
using DotYou.Types.Circle;
using System;
using System.Threading.Tasks;

namespace DotYou.Kernel.Services.Circle
{

    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public interface ICircleNetworkService
    {

        /// <summary>
        /// Sends a <see cref="ConnectionRequest"/> as an invitation.
        /// </summary>
        /// <returns></returns>

        Task SendConnectionRequest(ConnectionRequestHeader header);

        /// <summary>
        /// Establishes a connection between two individuals.  This should be called when
        /// from a recipient who has accpeted a sender's connection request
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task EstablishConnection(EstablishConnectionRequest request);

        /// <summary>
        /// Accepts a connection request.  This will store the public key certificate 
        /// of the sender then send the recipients public key certificate to the sender.
        /// </summary>
        Task AcceptConnectionRequest(Guid id);

        /// <summary>
        /// Get outgoing requests awaiting approval by their recipient
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<ConnectionRequest>> GetSentRequests(PageOptions pageRequest);

        /// <summary>
        /// Gets a sent <see cref="ConnectionRequest"/> by Id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<ConnectionRequest> GetSentRequest(Guid id);

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
        /// Stores an new incoming request that is not yet accepted.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task ReceiveConnectionRequest(ConnectionRequest request);

        /// <summary>
        /// Deletes a pending request.  This is useful if the user decides to ignore a request.
        /// </summary>
        Task DeletePendingRequest(Guid id);

        Task<PublicProfile> GetPublicInfo(string dotYouId);
    }
}
