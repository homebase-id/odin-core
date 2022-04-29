using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Identity.DataAttribute;
using Youverse.Core.Services.Authorization.Exchange;

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
        /// Gets connections from the system.
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        Task<PagedResult<IdentityConnectionRegistration>> GetConnections(PageOptions req);

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
        /// Gets the connection info if the specified <param name="remoteIdentityConnectionKey">xtoken half key</param> is valid
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <param name="remoteIdentityConnectionKey"></param>
        /// <returns></returns>
        Task<IdentityConnectionRegistration> GetIdentityConnectionRegistration(DotYouIdentity dotYouId, SensitiveByteArray remoteIdentityConnectionKey);

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
        /// <param name="name">The initial name information used at the time the request was accepted</param>
        /// <returns></returns>
        Task Connect(string dotYouId, NameAttribute name, ExchangeRegistration xtoken, SensitiveByteArray remoteHalfKey, SensitiveByteArray remoteSharedSecret);


        /// <summary>
        /// Gets profiles that have been marked as <see cref="ConnectionStatus.Blocked"/>
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        Task<PagedResult<DotYouProfile>> GetBlockedProfiles(PageOptions req);

        /// <summary>
        /// Gets connections which have been marked as <see cref="ConnectionStatus.Blocked"/>
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        Task<PagedResult<IdentityConnectionRegistration>> GetBlockedConnections(PageOptions req);

        /// <summary>
        /// Clears any connection regardless of <see cref="ConnectionStatus"/>.
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task DeleteConnection(DotYouIdentity dotYouId);
    }
}