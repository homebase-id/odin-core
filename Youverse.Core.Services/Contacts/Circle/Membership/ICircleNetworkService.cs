using System;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Contacts.Circle.Membership
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
        /// Returns a list of <see cref="DotYouProfile"/>s which are connected to this DI
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        Task<PagedResult<DotYouProfile>> GetConnectedProfiles(PageOptions req);

        /// <summary>
        /// Gets the current connection info
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task<IdentityConnectionRegistration> GetIdentityConnectionRegistration(DotYouIdentity dotYouId, bool overrideHack = false);

        /// <summary>
        /// Gets the connection info if the specified <param name="remoteClientAuthenticationToken">xtoken half key</param> is valid
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <param name="remoteClientAuthenticationToken"></param>
        /// <returns></returns>
        Task<IdentityConnectionRegistration> GetIdentityConnectionRegistration(DotYouIdentity dotYouId, ClientAuthenticationToken remoteClientAuthenticationToken);

        /// <summary>
        /// Gets the connection info if the specified key store key is valid.
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <param name="keyStoreKey"></param>
        /// <returns></returns>
        Task<IdentityConnectionRegistration> GetIdentityConnectionRegistrationWithKeyStoreKey(DotYouIdentity dotYouId, SensitiveByteArray keyStoreKey);

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
        /// <param name="registration">The connection info to be checked</param>
        /// <returns></returns>
        void AssertConnectionIsNoneOrValid(IdentityConnectionRegistration registration);

        /// <summary>
        /// Adds the specified dotYouId to your network
        /// </summary>
        /// <param name="dotYouId">The public key certificate containing the domain name which will be connected</param>
        /// <param name="accessRegistrationId">The id of the <see cref="AccessRegistration"/> that connects to the access this connection will have</param>
        /// <param name="remoteClientAccessToken">The keys used when accessing the remote identity</param>
        /// <returns></returns>
        Task Connect(string dotYouId, Guid accessRegistrationId, ClientAccessToken remoteClientAccessToken);
        
        /// <summary>
        /// Gets profiles that have been marked as <see cref="ConnectionStatus.Blocked"/>
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        Task<PagedResult<DotYouProfile>> GetBlockedProfiles(PageOptions req);

        /// <summary>
        /// Gets the access registration granted to the <param name="dotYouId"></param>
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <param name="remoteIdentityConnectionKey"></param>
        /// <returns></returns>
        Task<AccessRegistration> GetIdentityConnectionAccessRegistration(DotYouIdentity dotYouId, SensitiveByteArray remoteIdentityConnectionKey);
    }
}