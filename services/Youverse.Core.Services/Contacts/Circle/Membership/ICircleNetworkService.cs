using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Requests;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public interface ICircleNetworkService
    {
        /// <summary>
        /// Disconnects you from the specified <see cref="OdinId"/>
        /// </summary>
        /// <param name="odinId"></param>
        /// <returns></returns>
        Task<bool> Disconnect(OdinId odinId);

        /// <summary>
        /// Blocks the specified <see cref="OdinId"/> from your network
        /// </summary>
        /// <param name="odinId"></param>
        /// <returns></returns>
        Task<bool> Block(OdinId odinId);

        /// <summary>
        /// Unblocks the specified <see cref="OdinId"/> from your network
        /// </summary>
        /// <param name="odinId"></param>
        /// <returns></returns>
        Task<bool> Unblock(OdinId odinId);

        /// <summary>
        /// Returns a list of identities which are connected to this DI
        /// </summary>
        Task<CursoredResult<long, IdentityConnectionRegistration>> GetConnectedIdentities(int count, long cursor);

        /// <summary>
        /// Gets the current connection info
        /// </summary>
        /// <param name="odinId"></param>
        /// <returns></returns>
        Task<IdentityConnectionRegistration>
            GetIdentityConnectionRegistration(OdinId odinId, bool overrideHack = false);

        /// <summary>
        /// Gets the connection info if the specified <param name="remoteClientAuthenticationToken">xtoken half key</param> is valid
        /// </summary>
        /// <param name="odinId"></param>
        /// <param name="remoteClientAuthenticationToken"></param>
        /// <returns></returns>
        Task<IdentityConnectionRegistration> GetIdentityConnectionRegistration(OdinId odinId,
            ClientAuthenticationToken remoteClientAuthenticationToken);

        /// <summary>
        /// Determines if the specified odinId is connected 
        /// </summary>
        /// <param name="odinId"></param>
        /// <returns></returns>
        Task<bool> IsConnected(OdinId odinId);

        /// <summary>
        /// Throws an exception if the odinId is blocked.
        /// </summary>
        /// <param name="odinId"></param>
        /// <returns></returns>
        Task AssertConnectionIsNoneOrValid(OdinId odinId);

        /// <summary>
        /// Throws an exception if the odinId is blocked.
        /// </summary>
        /// <param name="registration">The connection info to be checked</param>
        /// <returns></returns>
        void AssertConnectionIsNoneOrValid(IdentityConnectionRegistration registration);

        /// <summary>
        /// Adds the specified odinId to your network
        /// </summary>
        /// <param name="odinIdentity">The public key certificate containing the domain name which will be connected</param>
        /// <param name="accessGrant">The access to be given to this connection</param>
        /// <param name="remoteClientAccessToken">The keys used when accessing the remote identity</param>
        /// <param name="handshakeResponseContactData"></param>
        /// <returns></returns>
        Task Connect(string odinIdentity, AccessExchangeGrant accessGrant, ClientAccessToken remoteClientAccessToken,
            ContactRequestData handshakeResponseContactData);

        /// <summary>
        /// Gets profiles that have been marked as <see cref="ConnectionStatus.Blocked"/>
        /// </summary>
        Task<CursoredResult<long, IdentityConnectionRegistration>> GetBlockedProfiles(int count, long cursor);

        /// <summary>
        /// Gets the access registration granted to the <param name="odinId"></param>
        /// </summary>
        /// <param name="odinId"></param>
        /// <param name="remoteIdentityConnectionKey"></param>
        /// <returns></returns>
        Task<AccessRegistration> GetIdentityConnectionAccessRegistration(OdinId odinId,
            SensitiveByteArray remoteIdentityConnectionKey);

        /// <summary>
        /// Creates a <see cref="PermissionContext"/> for the specified caller based on their access
        /// </summary>
        /// <returns></returns>
        Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreateTransitPermissionContext(
            OdinId odinId, ClientAuthenticationToken clientAuthToken);


        /// <summary>
        /// Creates a caller and permission context for the caller based on the <see cref="IdentityConnectionRegistrationClient"/> resolved by the authToken
        /// </summary>
        Task<(CallerContext callerContext, PermissionContext permissionContext)> CreateConnectedYouAuthClientContext(
            ClientAuthenticationToken authToken);

        /// <summary>
        /// Grants the odinId access to the drives and permissions of the specified circle
        /// </summary>
        Task GrantCircle(GuidId circleId, OdinId odinId);

        /// <summary>
        /// Removes drives and permissions of the specified circle from the odinId
        /// </summary>
        Task RevokeCircleAccess(GuidId circleId, OdinId odinId);

        Task<IEnumerable<OdinId>> GetCircleMembers(GuidId circleId);

        Task<Dictionary<Guid, CircleGrant>> CreateCircleGrantList(List<GuidId> circleIds,
            SensitiveByteArray keyStoreKey);

        //TODO: need to create a dedicated type for appgrants
        Task<Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>> CreateAppCircleGrantList(List<GuidId> circleIds,
            SensitiveByteArray keyStoreKey);

        /// <summary>
        /// Creates a circle definition
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task CreateCircleDefinition(CreateCircleRequest request);

        /// <summary>
        /// Gets a circle definition
        /// </summary>
        /// <param name="circleId"></param>
        /// <returns></returns>
        CircleDefinition GetCircleDefinition(GuidId circleId);

        /// <summary>
        /// Gets a list of all circle definitions
        /// </summary>
        /// <returns></returns>
        Task<IEnumerable<CircleDefinition>> GetCircleDefinitions(bool includeSystemCircle);

        /// <summary>
        /// Updates a <see cref="CircleDefinition"/> and applies permission and drive changes to all existing circle members
        /// </summary>
        /// <param name="circleDefinition"></param>
        /// <returns></returns>
        Task UpdateCircleDefinition(CircleDefinition circleDefinition);

        /// <summary>
        /// Tests if a circle has members and indicates if it can be deleted
        /// </summary>
        Task DeleteCircleDefinition(GuidId circleId);

        /// <summary>
        /// Disables a circle without removing it.  The grants provided by the circle will not be available to the members
        /// </summary>
        /// <param name="circleId"></param>
        /// <returns></returns>
        Task DisableCircle(GuidId circleId);

        /// <summary>
        /// Enables a circle
        /// </summary>
        /// <param name="circleId"></param>
        /// <returns></returns>
        Task EnableCircle(GuidId circleId);

        /// <summary>
        /// Creates a client for the IdentityConnectionRegistration
        /// </summary>
        /// <returns></returns>
        Task<bool> TryCreateIdentityConnectionClient(string odinId, ClientAuthenticationToken remoteIcrClientAuthToken,
            out ClientAccessToken clientAccessToken);

        /// <summary>
        /// Returns the <see cref="IdentityConnectionRegistrationClient"/> 
        /// </summary>
        /// <param name="authToken"></param>
        /// <returns></returns>
        Task<IdentityConnectionRegistrationClient> GetIdentityConnectionClient(ClientAuthenticationToken authToken);

        /// <summary>
        /// Creates the system circle
        /// </summary>
        /// <returns></returns>
        Task CreateSystemCircle();
    }
}