using System;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrantRedux;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Notification;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// <inheritdoc cref="ICircleNetworkService"/>
    /// </summary>
    public class CircleNetworkService : ICircleNetworkService
    {
        const string CONNECTIONS = "cnncts";

        private readonly ISystemStorage _systemStorage;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly ExchangeGrantServiceRedux _exchangeGrantService;

        public CircleNetworkService(DotYouContextAccessor contextAccessor, ILogger<ICircleNetworkService> logger, ISystemStorage systemStorage,
            IDotYouHttpClientFactory dotYouHttpClientFactory, ExchangeGrantServiceRedux exchangeGrantService)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _exchangeGrantService = exchangeGrantService;
        }

        public async Task UpdateConnectionProfileCache(DotYouIdentity dotYouId)
        {
            //updates a local cache of profile data
            //HACK: use the override to get the connection auth token
            var clientAuthToken = await this.GetConnectionAuthToken(dotYouId, true, true);
            var client = _dotYouHttpClientFactory.CreateClientUsingAccessToken<ICircleNetworkProfileCacheClient>(dotYouId, clientAuthToken);

            var response = await client.GetProfile(Guid.Empty);
            if (response.IsSuccessStatusCode)
            {
            }
        }

        public async Task<ClientAuthenticationToken> GetConnectionAuthToken(DotYouIdentity dotYouId, bool failIfNotConnected, bool overrideHack = false)
        {
            //TODO: need to NOT use the override version of GetIdentityConnectionRegistration but rather pass in some identifying token?
            var identityReg = await this.GetIdentityConnectionRegistration(dotYouId, overrideHack);
            if (!identityReg.IsConnected() && failIfNotConnected)
            {
                throw new YouverseSecurityException("Must be connected to perform this operation");
            }

            return identityReg.CreateClientAuthToken();
        }

        public async Task HandleNotification(DotYouIdentity senderDotYouId, CircleNetworkNotification notification)
        {
            if (notification.TargetSystemApi != SystemApi.CircleNetwork)
            {
                throw new Exception("Invalid notification type");
            }

            //TODO: thse should go into a background queue for processing offline.
            // processing them here means they're going to be called using the senderDI's context

            //TODO: need to expand on these numbers by using an enum or something
            if (notification.NotificationId == (int)CircleNetworkNotificationType.ProfileDataChanged)
            {
                await this.UpdateConnectionProfileCache(senderDotYouId);
            }

            throw new Exception($"Unknown notification Id {notification.NotificationId}");
        }

        public async Task<(bool isConnected, PermissionContext permissionContext)> CreatePermissionContext(DotYouIdentity callerDotYouId, ClientAuthenticationToken authToken)
        {
            var icr = await this.GetIdentityConnectionRegistration(callerDotYouId, authToken);
            var accessGrant = icr.AccessGrant;

            if (null == accessGrant || null == accessGrant.Grant)
            {
                throw new YouverseSecurityException("Invalid token");
            }

            if (accessGrant.AccessRegistration.IsRevoked || accessGrant.Grant.IsRevoked)
            {
                throw new YouverseSecurityException("Invalid token");
            }

            var permissionCtx = await _exchangeGrantService.CreatePermissionContext(authToken, accessGrant.Grant, accessGrant.AccessRegistration, false);
            return (icr.IsConnected(), permissionCtx);
        }

        public async Task<bool> Disconnect(DotYouIdentity dotYouId)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetIdentityConnectionRegistration(dotYouId);
            if (info is { Status: ConnectionStatus.Connected })
            {
                //destroy all access
                info.AccessGrant = null;
                info.Status = ConnectionStatus.None;
                _systemStorage.WithTenantSystemStorage<IdentityConnectionRegistration>(CONNECTIONS, s => s.Save(info));

                return true;
            }

            return false;
        }

        public async Task<bool> Block(DotYouIdentity dotYouId)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetIdentityConnectionRegistration(dotYouId);

            //TODO: when you block a connection, you must also destroy exchange grant

            if (null != info && info.Status == ConnectionStatus.Connected)
            {
                info.Status = ConnectionStatus.Blocked;
                _systemStorage.WithTenantSystemStorage<IdentityConnectionRegistration>(CONNECTIONS, s => s.Save(info));
                return true;
            }

            return false;
        }

        public async Task<bool> Unblock(DotYouIdentity dotYouId)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetIdentityConnectionRegistration(dotYouId);
            if (null != info && info.Status == ConnectionStatus.Blocked)
            {
                info.Status = ConnectionStatus.Connected;
                _systemStorage.WithTenantSystemStorage<IdentityConnectionRegistration>(CONNECTIONS, s => s.Save(info));
                return true;
            }

            return false;
        }

        public async Task<PagedResult<DotYouProfile>> GetBlockedProfiles(PageOptions req)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();
            var connectionsPage = await this.GetConnections(req, ConnectionStatus.Blocked);
            var page = new PagedResult<DotYouProfile>(
                connectionsPage.Request,
                connectionsPage.TotalPages,
                connectionsPage.Results.Select(c => new DotYouProfile()
                {
                    DotYouId = c.DotYouId,
                }).ToList());

            return page;
        }

        public async Task<PagedResult<DotYouProfile>> GetConnectedProfiles(PageOptions req)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(SystemApi.CircleNetwork, (int)CircleNetworkPermissions.Read);

            var connectionsPage = await this.GetConnections(req, ConnectionStatus.Connected);
            var page = new PagedResult<DotYouProfile>(
                connectionsPage.Request,
                connectionsPage.TotalPages,
                connectionsPage.Results.Select(c => new DotYouProfile()
                {
                    DotYouId = c.DotYouId,
                }).ToList());

            return page;
        }

        public async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistration(DotYouIdentity dotYouId, bool overrideHack = false)
        {
            //TODO: need to cache here?
            //HACK: DOING THIS WHILE DESIGNING XTOKEN - REMOVE THIS
            if (!overrideHack)
            {
                _contextAccessor.GetCurrent().AssertCanManageConnections();
            }

            return await GetIdentityConnectionRegistrationInternal(dotYouId);
        }

        public async Task<ClientAuthenticationToken> GetConnectionAuthToken(DotYouIdentity dotYouId)
        {
            var identityReg = await this.GetIdentityConnectionRegistration(dotYouId, true);
            if (!identityReg.IsConnected())
            {
                //TODO: throwing an exception here would result in a partial send.  need to return an error code and status instead
                throw new MissingDataException("Cannot send transfer a recipient to which you're not connected.");
            }

            return identityReg.CreateClientAuthToken();
        }

        public async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistration(DotYouIdentity dotYouId, ClientAuthenticationToken remoteClientAuthenticationToken)
        {
            var connection = await GetIdentityConnectionRegistrationInternal(dotYouId);

            if (connection?.AccessGrant.AccessRegistration == null)
            {
                throw new YouverseSecurityException("Unauthorized Action");
            }

            connection.AccessGrant.AccessRegistration.AssertValidRemoteKey(remoteClientAuthenticationToken.AccessTokenHalfKey);

            return connection;
        }

        public async Task<AccessRegistration> GetIdentityConnectionAccessRegistration(DotYouIdentity dotYouId, SensitiveByteArray remoteIdentityConnectionKey)
        {
            var connection = await GetIdentityConnectionRegistrationInternal(dotYouId);

            if (connection?.AccessGrant.AccessRegistration == null || connection?.IsConnected() == false)
            {
                throw new YouverseSecurityException("Unauthorized Action");
            }

            connection.AccessGrant.AccessRegistration.AssertValidRemoteKey(remoteIdentityConnectionKey);

            return connection.AccessGrant.AccessRegistration;
        }

        public async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistrationWithKeyStoreKey(DotYouIdentity dotYouId, SensitiveByteArray exchangeRegistrationKsk)
        {
            throw new NotImplementedException();
            //
            // var connection = await GetIdentityConnectionRegistrationInternal(dotYouId);
            //
            // if (connection?.ExchangeRegistration == null)
            // {
            //     throw new YouverseSecurityException("Unauthorized Action");
            // }
            //
            // //TODO: make this an explicit assert
            // //this will fail if the xtoken and exchange registation are incorrect
            // connection.ExchangeRegistration.KeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref exchangeRegistrationKsk);
            //
            // return connection;
        }

        private async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistrationInternal(DotYouIdentity dotYouId)
        {
            var info = await _systemStorage.WithTenantSystemStorageReturnSingle<IdentityConnectionRegistration>(CONNECTIONS, s => s.Get(dotYouId));

            if (null == info)
            {
                return new IdentityConnectionRegistration()
                {
                    DotYouId = dotYouId,
                    Status = ConnectionStatus.None,
                    LastUpdated = -1
                };
            }

            return info;
        }

        public async Task<bool> IsConnected(DotYouIdentity dotYouId)
        {
            //allow the caller to see if s/he is connected, otherwise 
            if (_contextAccessor.GetCurrent().Caller.DotYouId != dotYouId)
            {
                //TODO: this needs to be changed to - can view connections
                _contextAccessor.GetCurrent().AssertCanManageConnections();
            }

            var info = await this.GetIdentityConnectionRegistration(dotYouId);
            return info.Status == ConnectionStatus.Connected;
        }

        public async Task AssertConnectionIsNoneOrValid(DotYouIdentity dotYouId)
        {
            var info = await this.GetIdentityConnectionRegistration(dotYouId);
            this.AssertConnectionIsNoneOrValid(info);
        }

        public void AssertConnectionIsNoneOrValid(IdentityConnectionRegistration registration)
        {
            if (registration.Status == ConnectionStatus.Blocked)
            {
                throw new SecurityException("DotYouId is blocked");
            }
        }

        public async Task Connect(string dotYouIdentity, AccessExchangeGrant accessGrant, ClientAccessToken remoteClientAccessToken)
        {
            //TODO: need to add security that this method can be called

            var dotYouId = (DotYouIdentity)dotYouIdentity;

            //1. validate current connection state
            var info = await this.GetIdentityConnectionRegistrationInternal(dotYouId);

            if (info.Status != ConnectionStatus.None)
            {
                throw new YouverseSecurityException("invalid connection state");
            }

            //TODO: need to scan the YouAuthService to see if this user has a YouAuthRegistration

            await this.StoreConnection(dotYouId, accessGrant, remoteClientAccessToken);
        }

        private async Task StoreConnection(string dotYouIdentity, AccessExchangeGrant accessGrant, ClientAccessToken remoteClientAccessToken)
        {
            var dotYouId = (DotYouIdentity)dotYouIdentity;

            //2. add the record to the list of connections
            var newConnection = new IdentityConnectionRegistration()
            {
                DotYouId = dotYouId,
                Status = ConnectionStatus.Connected,
                LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                AccessGrant = accessGrant,
                ClientAccessTokenId = remoteClientAccessToken.Id,
                ClientAccessTokenHalfKey = remoteClientAccessToken.AccessTokenHalfKey.GetKey(),
                ClientAccessTokenSharedSecret = remoteClientAccessToken.SharedSecret.GetKey()
            };

            _systemStorage.WithTenantSystemStorage<IdentityConnectionRegistration>(CONNECTIONS, s => s.Save(newConnection));

            //TODO: the following is a good place for the mediatr pattern
            //tell the profile service to refresh the attributes?
            //send notification to clients
        }

        private async Task<PagedResult<IdentityConnectionRegistration>> GetConnections(PageOptions req, ConnectionStatus status)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            Expression<Func<IdentityConnectionRegistration, string>> sortKeySelector = key => key.DotYouId;
            Expression<Func<IdentityConnectionRegistration, bool>> predicate = id => id.Status == status;
            PagedResult<IdentityConnectionRegistration> results =
                await _systemStorage.WithTenantSystemStorageReturnList<IdentityConnectionRegistration>(CONNECTIONS, s => s.Find(predicate, ListSortDirection.Ascending, sortKeySelector, req));
            return results;
        }
    }
}