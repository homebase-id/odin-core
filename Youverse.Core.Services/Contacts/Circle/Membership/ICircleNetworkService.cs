﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Notification;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public interface ICircleNetworkService
    {
        /// <summary>
        /// Updates the local cache of profile data for a given connection
        /// </summary>
        /// <returns></returns>
        Task UpdateConnectionProfileCache(DotYouIdentity dotYouId);

        /// <summary>
        /// Gets the <see cref="ClientAuthenticationToken"/> for a given connection
        /// </summary>
        /// <returns></returns>
        Task<ClientAuthenticationToken> GetConnectionAuthToken(DotYouIdentity dotYouId, bool failIfNotConnected = true, bool overrideHack = false);

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
        Task<PagedResult<DotYouProfile>> GetConnectedIdentities(PageOptions req);

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
        /// <param name="accessGrant">The access to be given to this connection</param>
        /// <param name="remoteClientAccessToken">The keys used when accessing the remote identity</param>
        /// <returns></returns>
        Task Connect(string dotYouId, AccessExchangeGrant accessGrant, ClientAccessToken remoteClientAccessToken);

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

        /// <summary>
        /// Handles the incoming notification.
        /// </summary>
        Task HandleNotification(DotYouIdentity senderDotYouId, CircleNetworkNotification notification);

        /// <summary>
        /// Creates a <see cref="PermissionContext"/> for the specified caller based on their access
        /// </summary>
        /// <returns></returns>
        Task<(PermissionContext permissionContext, List<ByteArrayId> circleIds)> CreateTransitPermissionContext(DotYouIdentity dotYouId, ClientAuthenticationToken clientAuthToken);
        
        /// <summary>
        /// Creates a <see cref="PermissionContext"/> for the specified caller based on the <see cref="IdentityConnectionRegistrationClient"/> resolved by the clientAuthToken
        /// </summary>
        /// <returns></returns>
        Task<(DotYouIdentity dotYouId, bool isAuthenticated, bool isConnected, PermissionContext permissionContext, List<ByteArrayId> circleIds)> CreateClientPermissionContext(ClientAuthenticationToken authToken);

        /// <summary>
        /// Grants the dotYouId access to the drives and permissions of the specified circle
        /// </summary>
        Task GrantCircle(ByteArrayId circleId, DotYouIdentity dotYouId);

        /// <summary>
        /// Removes drives and permissions of the specified circle from the dotYouId
        /// </summary>
        Task RevokeCircleAccess(ByteArrayId circleId, DotYouIdentity dotYouId);

        Task<IEnumerable<DotYouIdentity>> GetCircleMembers(ByteArrayId circleId);

        Task<Dictionary<string, CircleGrant>> CreateCircleGrantList(List<ByteArrayId> circleIds, SensitiveByteArray keyStoreKey);

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
        CircleDefinition GetCircleDefinition(ByteArrayId circleId);

        /// <summary>
        /// Gets a list of all circle definitions
        /// </summary>
        /// <returns></returns>
        Task<IEnumerable<CircleDefinition>> GetCircleDefinitions();

        /// <summary>
        /// Updates a <see cref="CircleDefinition"/> and applies permission and drive changes to all existing circle members
        /// </summary>
        /// <param name="circleDefinition"></param>
        /// <returns></returns>
        Task UpdateCircleDefinition(CircleDefinition circleDefinition);

        /// <summary>
        /// Tests if a circle has members and indicates if it can be deleted
        /// </summary>
        Task DeleteCircleDefinition(ByteArrayId circleId);

        /// <summary>
        /// Disables a circle without removing it.  The grants provided by the circle will not be available to the members
        /// </summary>
        /// <param name="circleId"></param>
        /// <returns></returns>
        Task DisableCircle(ByteArrayId circleId);
        
        /// <summary>
        /// Enables a circle
        /// </summary>
        /// <param name="circleId"></param>
        /// <returns></returns>
        Task EnableCircle(ByteArrayId circleId);

        /// <summary>
        /// Creates a client for the IdentityConnectionRegistration
        /// </summary>
        /// <returns></returns>
        Task<bool> TryCreateIdentityConnectionClient(string dotYouId, ClientAuthenticationToken remoteIcrClientAuthToken, out ClientAccessToken clientAccessToken);

        /// <summary>
        /// Returns the <see cref="IdentityConnectionRegistrationClient"/> 
        /// </summary>
        /// <param name="authToken"></param>
        /// <returns></returns>
        Task<IdentityConnectionRegistrationClient> GetIdentityConnectionClient(ClientAuthenticationToken authToken);

    }
}