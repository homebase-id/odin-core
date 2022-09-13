using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Notification;
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.KeyValue;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// <inheritdoc cref="ICircleNetworkService"/>
    /// </summary>
    public class CircleNetworkService : ICircleNetworkService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly CircleNetworkStorage _storage;
        private readonly CircleDefinitionService _circleDefinitionService;
        private readonly TableCircleMember _circleMemberStorage;

        public CircleNetworkService(DotYouContextAccessor contextAccessor, ILogger<ICircleNetworkService> logger, ISystemStorage systemStorage,
            IDotYouHttpClientFactory dotYouHttpClientFactory, ExchangeGrantService exchangeGrantService, TenantContext tenantContext, CircleDefinitionService circleDefinitionService)
        {
            _contextAccessor = contextAccessor;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _exchangeGrantService = exchangeGrantService;
            _circleDefinitionService = circleDefinitionService;

            _storage = new CircleNetworkStorage(tenantContext.StorageConfig.DataStoragePath);

            _circleMemberStorage = new TableCircleMember(systemStorage.GetDBInstance());
            _circleMemberStorage.EnsureTableExists(false);
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

        public async Task<(bool isConnected, PermissionContext permissionContext, List<ByteArrayId> circleIds)> CreatePermissionContext(DotYouIdentity callerDotYouId,
            ClientAuthenticationToken authToken)
        {
            var icr = await this.GetIdentityConnectionRegistration(callerDotYouId, authToken);
            var accessGrant = icr.AccessGrant;

            if (null == accessGrant || !accessGrant.IsValid())
            {
                throw new YouverseSecurityException("Invalid token");
            }

            //note: duplicate code in YouAuthRegistrationService
            var grants = new Dictionary<string, ExchangeGrant>();
            var enabledCircles = new List<ByteArrayId>();
            foreach (var kvp in accessGrant.CircleGrants)
            {
                var cg = kvp.Value;
                if (_circleDefinitionService.IsEnabled(cg.CircleId))
                {
                    enabledCircles.Add(cg.CircleId);
                    var xGrant = new ExchangeGrant()
                    {
                        Created = 0,
                        Modified = 0,
                        IsRevoked = false, //TODO
                        KeyStoreKeyEncryptedDriveGrants = cg.KeyStoreKeyEncryptedDriveGrants,
                        MasterKeyEncryptedKeyStoreKey = accessGrant.MasterKeyEncryptedKeyStoreKey,
                        PermissionSet = cg.PermissionSet
                    };
                    grants.Add(kvp.Key, xGrant);
                }
            }

            var permissionCtx = await _exchangeGrantService.CreatePermissionContext(authToken, grants, accessGrant.AccessRegistration, false);
            return (icr.IsConnected(), permissionCtx, enabledCircles);
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
                this.SaveIcr(info);
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
                this.SaveIcr(info);
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
                this.SaveIcr(info);
                return true;
            }

            return false;
        }

        public async Task<PagedResult<DotYouProfile>> GetBlockedProfiles(PageOptions req)
        {
            var connectionsPage = await this.GetConnectionsInternal(req, ConnectionStatus.Blocked);
            var page = new PagedResult<DotYouProfile>(
                connectionsPage.Request,
                connectionsPage.TotalPages,
                connectionsPage.Results.Select(c => new DotYouProfile()
                {
                    DotYouId = c.DotYouId,
                }).ToList());

            return page;
        }

        public async Task<PagedResult<DotYouProfile>> GetConnectedIdentities(PageOptions req)
        {
            var connectionsPage = await this.GetConnectionsInternal(req, ConnectionStatus.Connected);
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

        private async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistrationInternal(DotYouIdentity dotYouId)
        {
            var info = _storage.Get(dotYouId);

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

        public async Task<bool> IsCircleMember(ByteArrayId circleId, DotYouIdentity dotYouId)
        {
            // _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionFlags.ReadCircleMembership);
            var icr = await this.GetIdentityConnectionRegistrationInternal(dotYouId);
            return icr.AccessGrant.CircleGrants.ContainsKey(circleId.ToBase64());
        }

        public async Task<IEnumerable<DotYouIdentity>> GetCircleMembers(ByteArrayId circleId)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);

            //Note: this list is a cache of members for a circle.  the source of truth is the IdentityConnectionRegistration.AccessExchangeGrant.CircleGrants property for each DotYouIdentity
            var memberBytesList = _circleMemberStorage.GetMembers(circleId);
            return memberBytesList.Select(id => DotYouIdentity.FromByteArrayId(new ByteArrayId(id)));
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

            //2. add the record to the list of connections
            var newConnection = new IdentityConnectionRegistration()
            {
                DotYouId = dotYouId,
                Status = ConnectionStatus.Connected,
                Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                AccessGrant = accessGrant,
                ClientAccessTokenId = remoteClientAccessToken.Id,
                ClientAccessTokenHalfKey = remoteClientAccessToken.AccessTokenHalfKey.GetKey(),
                ClientAccessTokenSharedSecret = remoteClientAccessToken.SharedSecret.GetKey()
            };

            this.SaveIcr(newConnection);

            foreach (var kvp in newConnection.AccessGrant.CircleGrants)
            {
                var circleId = kvp.Value.CircleId.Value;
                var dotYouIdBytes = dotYouId.ToByteArrayId().Value;
                var circleMembers = _circleMemberStorage.GetMembers(circleId);
                var isMember = circleMembers.Any(id => id.SequenceEqual(dotYouIdBytes));
                if (!isMember)
                {
                    _circleMemberStorage.AddMembers(circleId, new List<byte[]>() { dotYouIdBytes });
                }
            }
        }
        
        public async Task GrantCircle(ByteArrayId circleId, DotYouIdentity dotYouId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var icr = await this.GetIdentityConnectionRegistrationInternal(dotYouId);

            if (icr == null || !icr.IsConnected())
            {
                throw new YouverseSecurityException($"{dotYouId} must have valid connection to be added to a circle");
            }

            if (icr.AccessGrant.CircleGrants.TryGetValue(circleId.ToBase64(), out var _))
            {
                //TODO: Here we should ensure it's in the _circleMemberStorage just in case this was called because it's out of sync
                throw new YouverseException($"{dotYouId} is already member of circle");
            }

            var circleDefinition = _circleDefinitionService.GetCircle(circleId);
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
            var circleGrant = await this.CreateCircleGrant(circleDefinition, keyStoreKey, masterKey);
            keyStoreKey.Wipe();

            icr.AccessGrant.CircleGrants.Add(circleGrant.CircleId.ToBase64(), circleGrant);

            _circleMemberStorage.AddMembers(circleId.Value, new List<byte[]>() { dotYouId.ToByteArrayId().Value });
            this.SaveIcr(icr);
        }


        public async Task<Dictionary<string, CircleGrant>> CreateCircleGrantList(List<ByteArrayId> circleIds, SensitiveByteArray keyStoreKey)
        {
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

            var circleGrants = new Dictionary<string, CircleGrant>();

            foreach (var id in circleIds ?? new List<ByteArrayId>())
            {
                var def = _circleDefinitionService.GetCircle(id);

                var cg = await this.CreateCircleGrant(def, keyStoreKey, masterKey);
                circleGrants.Add(id.ToBase64(), cg);
            }

            return circleGrants;
        }

        public Task CreateCircleDefinition(CreateCircleRequest request)
        {
            return _circleDefinitionService.Create(request);
        }

        private async Task<CircleGrant> CreateCircleGrant(CircleDefinition def, SensitiveByteArray keyStoreKey, SensitiveByteArray masterKey)
        {
            //map the exchange grant to a structure that matches ICR
            var grant = await _exchangeGrantService.CreateExchangeGrant(keyStoreKey, def.Permissions, def.DrivesGrants, masterKey);
            return new CircleGrant()
            {
                CircleId = def.Id,
                KeyStoreKeyEncryptedDriveGrants = grant.KeyStoreKeyEncryptedDriveGrants,
                PermissionSet = grant.PermissionSet,
            };
        }

        public async Task RevokeCircleAccess(ByteArrayId circleId, DotYouIdentity dotYouId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var icr = await this.GetIdentityConnectionRegistrationInternal(dotYouId);
            var circle64 = circleId.ToBase64();
            if (icr.AccessGrant.CircleGrants.ContainsKey(circle64))
            {
                if (!icr.AccessGrant.CircleGrants.Remove(circleId.ToBase64()))
                {
                    throw new YouverseException($"Failed to remove {circle64} from {dotYouId}");
                }
            }

            _circleMemberStorage.RemoveMembers(circleId, new List<byte[]>() { dotYouId.ToByteArrayId() });
            this.SaveIcr(icr);
        }

        public CircleDefinition GetCircleDefinition(ByteArrayId circleId)
        {
            Guard.Argument(circleId, nameof(circleId)).NotNull().Require(id => ByteArrayId.IsValid(id));
            var def = _circleDefinitionService.GetCircle(circleId);
            return def;
        }

        public Task<IEnumerable<CircleDefinition>> GetCircleDefinitions()
        {
            var circles = _circleDefinitionService.GetCircles();
            return circles;
        }

        public async Task UpdateCircleDefinition(CircleDefinition circleDefinition)
        {
            await ReconcileCircleGrant(circleDefinition);
            await _circleDefinitionService.Update(circleDefinition);
        }

        public async Task DeleteCircleDefinition(ByteArrayId circleId)
        {
            var members = await this.GetCircleMembers(circleId);

            if (members.Any())
            {
                throw new YouverseException("Cannot delete a circle with members");
            }

            await _circleDefinitionService.Delete(circleId);
        }

        //

        private async Task ReconcileCircleGrant(CircleDefinition circleDef)
        {
            Guard.Argument(circleDef, nameof(circleDef)).NotNull();

            var members = await GetCircleMembers(circleDef.Id);
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

            List<DotYouIdentity> invalidMembers = new List<DotYouIdentity>();
            foreach (var dotYouId in members)
            {
                var icr = await this.GetIdentityConnectionRegistrationInternal(dotYouId);

                var circleKey = circleDef.Id.ToBase64();
                var hasCg = icr.AccessGrant.CircleGrants.Remove(circleKey, out var oldCircleGrant);

                if (icr.IsConnected() && hasCg)
                {
                    var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
                    var newGrant = await this.CreateCircleGrant(circleDef, keyStoreKey, masterKey);
                    icr.AccessGrant.CircleGrants[circleKey] = newGrant;
                }
                else
                {
                    //It should not occur that a circle has a member
                    //who is not connected but let's capture it
                    invalidMembers.Add(dotYouId);
                }

                this.SaveIcr(icr);
            }

            //TODO: determine how to handle invalidMembers - do we return to the UI?  do we remove from all circles?
        }

        private async Task<PagedResult<IdentityConnectionRegistration>> GetConnectionsInternal(PageOptions req, ConnectionStatus status)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);

            var list = _storage.GetList().Where(icr => icr.Status == status);
            return new PagedResult<IdentityConnectionRegistration>(req, 1, list.ToList());
        }
        
        private void SaveIcr(IdentityConnectionRegistration icr)
        {
            //TODO: this is a critical change; need to audit this
            _storage.Upsert(icr);
        }
    }
}