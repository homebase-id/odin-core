﻿using DotYou.Types;
using System.Threading.Tasks;
using DotYou.Types.DataAttribute;

namespace DotYou.Kernel.Services.Circle
{
    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public interface ICircleNetworkService
    {
        /// <summary>
        /// Disconnects you from the specified <see cref="DotYouIdentity"/>
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task<bool> Disconnect(DotYouIdentity dotYouId);

        /// <summary>
        /// Blocks the specified <see cref="DotYouIdentity"/> from your network
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task<bool> Block(DotYouIdentity dotYouId);

        /// <summary>
        /// Unblocks the specified <see cref="DotYouIdentity"/> from your network
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task<bool> Unblock(DotYouIdentity dotYouId);
        
        /// <summary>
        /// Gets connections from the system.
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        Task<PagedResult<ConnectionInfo>> GetConnections(PageOptions req);

        /// <summary>
        /// Gets the current connection info
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task<ConnectionInfo> GetConnectionInfo(DotYouIdentity dotYouId);
        
        /// <summary>
        /// Determines if the specified dotYouId is connected 
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task<bool> IsConnected(DotYouIdentity dotYouId);

        /// <summary>
        /// Throws an exception if the dotYouId is blocked.
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task AssertConnectionIsNoneOrValid(DotYouIdentity dotYouId);
        
        /// <summary>
        /// Throws an exception if the dotYouId is blocked.
        /// </summary>
        /// <param name="info">The connection info to be checked</param>
        /// <returns></returns>
        void AssertConnectionIsNoneOrValid(ConnectionInfo info);
        
        /// <summary>
        /// Adds the specified dotYouId to your network
        /// </summary>
        /// <param name="publicKeyCertificate">The public key certificate containing the domain name which will be connected</param>
        /// <param name="name">The initial name information used at the time the request was accepted</param>
        /// <returns></returns>
        Task Connect(string publicKeyCertificate, NameAttribute name);
    }
}