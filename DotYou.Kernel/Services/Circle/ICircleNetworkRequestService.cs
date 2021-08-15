﻿using System;
using System.Threading.Tasks;
using DotYou.Types;
using DotYou.Types.Circle;

namespace DotYou.Kernel.Services.Circle
{
    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public interface ICircleNetworkRequestService
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
        Task EstablishConnection(AcknowledgedConnectionRequest request);

        /// <summary>
        /// Accepts a connection request.  This will store the public key certificate 
        /// of the sender then send the recipients public key certificate to the sender.
        /// </summary>
        Task AcceptConnectionRequest(DotYouIdentity sender);

        /// <summary>
        /// Get outgoing requests awaiting approval by their recipient
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<ConnectionRequest>> GetSentRequests(PageOptions pageRequest);

        /// <summary>
        /// Gets a connection request sent to the specified recipient
        /// </summary>
        /// <returns>Returns the <see cref="ConnectionRequest"/> if one exists, otherwise null</returns>
        Task<ConnectionRequest> GetSentRequest(DotYouIdentity recipient);

        /// <summary>
        /// Gets a list of requests awaiting approval.
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<ConnectionRequest>> GetPendingRequests(PageOptions pageRequest);

        /// <summary>
        /// Gets a pending request by its sender
        /// </summary>
        /// <returns></returns>
        Task<ConnectionRequest> GetPendingRequest(DotYouIdentity sender);
        
        /// <summary>
        /// Deletes the sent request record.  If the recipient accepts the request
        /// after it has been delete, the connection will not be established.
        /// 
        /// This does not notify the original recipient
        /// </summary>
        /// <param name="recipient"></param>
        /// <returns></returns>
        Task DeleteSentRequest(DotYouIdentity recipient);

        /// <summary>
        /// Stores an new incoming request that is not yet accepted.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task ReceiveConnectionRequest(ConnectionRequest request);

        /// <summary>
        /// Deletes a pending request.  This is useful if the user decides to ignore a request.
        /// </summary>
        Task DeletePendingRequest(DotYouIdentity sender);
    }
}