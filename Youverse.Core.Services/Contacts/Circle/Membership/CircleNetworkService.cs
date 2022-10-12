using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Notification;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.KeyValue;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// <inheritdoc cref="ICircleNetworkService"/>
    /// </summary>
    public class CircleNetworkService : ICircleNetworkService, INotificationHandler<DriveDefinitionAddedNotification>
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly CircleNetworkStorage _storage;
        private readonly CircleDefinitionService _circleDefinitionService;
        private readonly TableCircleMember _circleMemberStorage;
        private readonly TenantContext _tenantContext;

        private readonly GuidId _icrClientDataType = GuidId.FromString("__icr_client_reg");
        private readonly ThreeKeyValueStorage _icrClientValueStorage;

        public CircleNetworkService(DotYouContextAccessor contextAccessor, ILogger<ICircleNetworkService> logger, ISystemStorage systemStorage,
            IDotYouHttpClientFactory dotYouHttpClientFactory, ExchangeGrantService exchangeGrantService, TenantContext tenantContext, CircleDefinitionService circleDefinitionService)
        {
            _contextAccessor = contextAccessor;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _circleDefinitionService = circleDefinitionService;

            _storage = new CircleNetworkStorage(tenantContext.StorageConfig.DataStoragePath);

            _circleMemberStorage = new TableCircleMember(systemStorage.GetDBInstance());
            _circleMemberStorage.EnsureTableExists(false);

            _icrClientValueStorage = new ThreeKeyValueStorage(systemStorage.GetDBInstance().TblKeyThreeValue);
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

            throw new Exception($"Unknown notification Id {notification.NotificationId}");
        }

        public async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreateTransitPermissionContext(DotYouIdentity dotYouId, ClientAuthenticationToken authToken)
        {
            var icr = await this.GetIdentityConnectionRegistration(dotYouId, authToken);

            if (!icr.AccessGrant?.IsValid() ?? false)
            {
                throw new YouverseSecurityException("Invalid token");
            }

            if (!icr.IsConnected())
            {
                throw new YouverseSecurityException("Invalid connection");
            }

            var (permissionContext, enabledCircles) = await CreatePermissionContextInternal(icr.AccessGrant.CircleGrants, icr.AccessGrant.AccessRegistration, authToken);

            return (permissionContext, enabledCircles);
        }

        /// <summary>
        /// Creates a caller and permission context if the authToken represents a connected identity
        /// </summary>
        public async Task<(CallerContext callerContext, PermissionContext permissionContext)> CreateConnectedClientContext(
            ClientAuthenticationToken authToken)
        {
            var client = await this.GetIdentityConnectionClient(authToken);
            if (null == client)
            {
                throw new YouverseSecurityException("Invalid token");
            }
            
            client.AccessRegistration.AssertValidRemoteKey(authToken.AccessTokenHalfKey);

            var icr = await this.GetIdentityConnectionRegistrationInternal(client.DotYouId);
            bool isAuthenticated = icr.AccessGrant?.IsValid() ?? false;
            bool isConnected = icr.IsConnected();

            // Only return the permissions if the identity is connected.  
            if (isAuthenticated && isConnected)
            {
                var (permissionContext, enabledCircles) = await CreatePermissionContextInternal(icr.AccessGrant.CircleGrants, client.AccessRegistration, authToken);
                var cc = new CallerContext(
                    dotYouId: client.DotYouId,
                    masterKey: null,
                    securityLevel: SecurityGroupType.Connected,
                    circleIds: enabledCircles);

                return (cc, permissionContext);
            }

            // Otherwise, fall back to anonymous drives
            if (isAuthenticated)
            {
                var cc = new CallerContext(
                    dotYouId: client.DotYouId,
                    masterKey: null,
                    securityLevel: SecurityGroupType.Authenticated);

                List<int> permissionKeys = new List<int>() { };
                if (_tenantContext.TenantSystemConfig.AuthenticatedIdentitiesCanViewConnections)
                {
                    permissionKeys.Add(PermissionKeys.ReadConnections);
                }

                //create permission context with anonymous drives only
                var anonPermissionContext = await _exchangeGrantService.CreatePermissionContext(
                    authToken: authToken,
                    grants: null,
                    accessReg: icr.AccessGrant.AccessRegistration,
                    isOwner: false,
                    additionalPermissionKeys: permissionKeys);

                return (cc, anonPermissionContext);
            }

            throw new YouverseSecurityException("Invalid auth token");
        }

        public async Task<bool> Disconnect(DotYouIdentity dotYouId)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetIdentityConnectionRegistration(dotYouId);
            if (info is { Status: ConnectionStatus.Connected })
            {
                //destroy all access
                info.AccessGrant = null;

                //TODO: remove ICR clients

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

        public async Task<PagedResult<IdentityConnectionRegistration>> GetBlockedProfiles(PageOptions req)
        {
            var connectionsPage = await this.GetConnectionsInternal(req, ConnectionStatus.Blocked);
            return connectionsPage;
        }

        public async Task<PagedResult<IdentityConnectionRegistration>> GetConnectedIdentities(PageOptions req)
        {
            var connectionsPage = await this.GetConnectionsInternal(req, ConnectionStatus.Connected);
            return connectionsPage;
        }

        public async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistration(DotYouIdentity dotYouId, bool overrideHack = false)
        {
            //TODO: need to cache here?
            //HACK: DOING THIS WHILE DESIGNING XTOKEN - REMOVE THIS
            if (!overrideHack)
            {
                _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
            }

            return await GetIdentityConnectionRegistrationInternal(dotYouId);
        }

        public async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistration(DotYouIdentity dotYouId, ClientAuthenticationToken remoteClientAuthenticationToken)
        {
            var connection = await GetIdentityConnectionRegistrationInternal(dotYouId);

            if (connection?.AccessGrant?.AccessRegistration == null)
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

        public async Task<bool> IsCircleMember(GuidId circleId, DotYouIdentity dotYouId)
        {
            // _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionFlags.ReadCircleMembership);
            var icr = await this.GetIdentityConnectionRegistrationInternal(dotYouId);
            return icr.AccessGrant.CircleGrants.ContainsKey(circleId.ToBase64());
        }

        public async Task<IEnumerable<DotYouIdentity>> GetCircleMembers(GuidId circleId)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);

            //Note: this list is a cache of members for a circle.  the source of truth is the IdentityConnectionRegistration.AccessExchangeGrant.CircleGrants property for each DotYouIdentity
            var memberBytesList = _circleMemberStorage.GetMembers(circleId);
            return memberBytesList.Select(idBytes => DotYouIdentity.FromByteArray(idBytes));
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

        public async Task Connect(string dotYouIdentity, AccessExchangeGrant accessGrant, ClientAccessToken remoteClientAccessToken, ContactRequestData contactData)
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
                ClientAccessTokenSharedSecret = remoteClientAccessToken.SharedSecret.GetKey(),
                OriginalContactData = contactData
            };

            this.SaveIcr(newConnection);

            foreach (var kvp in newConnection.AccessGrant.CircleGrants)
            {
                var circleId = kvp.Value.CircleId;
                var dotYouIdBytes = dotYouId.ToByteArray();
                var circleMembers = _circleMemberStorage.GetMembers(circleId);
                var isMember = circleMembers.Any(id => id.SequenceEqual(dotYouIdBytes));
                if (!isMember)
                {
                    _circleMemberStorage.AddMembers(circleId, new List<byte[]>() { dotYouIdBytes });
                }
            }
        }

        /// <summary>
        /// Gives access to all resource granted by the specified circle to the dotYouId
        /// </summary>
        public async Task GrantCircle(GuidId circleId, DotYouIdentity dotYouId)
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

            _circleMemberStorage.AddMembers(circleId, new List<byte[]>() { dotYouId.ToByteArray() });
            this.SaveIcr(icr);
        }

        public async Task<Dictionary<string, CircleGrant>> CreateCircleGrantList(List<GuidId> circleIds, SensitiveByteArray keyStoreKey)
        {
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

            var circleGrants = new Dictionary<string, CircleGrant>();

            // Always put identities in the system circle
            var cids = circleIds ?? new List<GuidId>();
            cids.Add(CircleConstants.SystemCircleId);

            foreach (var id in cids ?? new List<GuidId>())
            {
                var def = _circleDefinitionService.GetCircle(id);

                var cg = await this.CreateCircleGrant(def, keyStoreKey, masterKey);
                circleGrants.Add(id.ToBase64(), cg);
            }

            return circleGrants;
        }

        public async Task RevokeCircleAccess(GuidId circleId, DotYouIdentity dotYouId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var icr = await this.GetIdentityConnectionRegistrationInternal(dotYouId);
            var circle64 = circleId.ToBase64();
            if (icr.AccessGrant?.CircleGrants.ContainsKey(circle64) ?? false)
            {
                if (!icr.AccessGrant.CircleGrants.Remove(circleId.ToBase64()))
                {
                    throw new YouverseException($"Failed to remove {circle64} from {dotYouId}");
                }
            }

            _circleMemberStorage.RemoveMembers(circleId, new List<byte[]>() { dotYouId.ToByteArray() });
            this.SaveIcr(icr);
        }

        public CircleDefinition GetCircleDefinition(GuidId circleId)
        {
            Guard.Argument(circleId, nameof(circleId)).NotNull().Require(id => GuidId.IsValid(id));
            var def = _circleDefinitionService.GetCircle(circleId);
            return def;
        }

        public async Task<IEnumerable<CircleDefinition>> GetCircleDefinitions(bool includeSystemCircle)
        {
            var circles = await _circleDefinitionService.GetCircles(includeSystemCircle);
            return circles;
        }

        public async Task CreateCircleDefinition(CreateCircleRequest request)
        {
            await _circleDefinitionService.Create(request);
        }

        public async Task UpdateCircleDefinition(CircleDefinition circleDef)
        {
            Guard.Argument(circleDef, nameof(circleDef)).NotNull();

            _circleDefinitionService.AssertValidDriveGrants(circleDef.DriveGrants);

            var members = await GetCircleMembers(circleDef.Id);
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

            List<DotYouIdentity> invalidMembers = new List<DotYouIdentity>();
            foreach (var dotYouId in members)
            {
                var icr = await this.GetIdentityConnectionRegistrationInternal(dotYouId);

                var circleKey = circleDef.Id.ToBase64();
                var hasCg = icr.AccessGrant.CircleGrants.Remove(circleKey, out _);

                if (icr.IsConnected() && hasCg)
                {
                    var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
                    icr.AccessGrant.CircleGrants[circleKey] = await this.CreateCircleGrant(circleDef, keyStoreKey, masterKey);
                    keyStoreKey.Wipe();
                }
                else
                {
                    //It should not occur that a circle has a member
                    //who is not connected but let's capture it
                    invalidMembers.Add(dotYouId);
                }

                this.SaveIcr(icr);
            }

            await _circleDefinitionService.Update(circleDef);

            //TODO: determine how to handle invalidMembers - do we return to the UI?  do we remove from all circles?
        }

        public async Task DeleteCircleDefinition(GuidId circleId)
        {
            var members = await this.GetCircleMembers(circleId);

            if (members.Any())
            {
                throw new YouverseException("Cannot delete a circle with members");
            }

            await _circleDefinitionService.Delete(circleId);
        }

        public Task DisableCircle(GuidId circleId)
        {
            var circle = _circleDefinitionService.GetCircle(circleId);
            circle.Disabled = true;
            circle.LastUpdated = UnixTimeUtcMilliseconds.Now().milliseconds;
            _circleDefinitionService.Update(circle);
            return Task.CompletedTask;
        }

        public Task EnableCircle(GuidId circleId)
        {
            var circle = _circleDefinitionService.GetCircle(circleId);
            circle.Disabled = false;
            circle.LastUpdated = UnixTimeUtcMilliseconds.Now().milliseconds;
            _circleDefinitionService.Update(circle);
            return Task.CompletedTask;
        }

        public Task<IdentityConnectionRegistrationClient> GetIdentityConnectionClient(ClientAuthenticationToken authToken)
        {
            var client = _icrClientValueStorage.Get<IdentityConnectionRegistrationClient>(authToken.Id);
            return Task.FromResult(client);
        }

        public Task CreateSystemCircle()
        {
            _circleDefinitionService.CreateSystemCircle();
            return Task.CompletedTask;
        }

        public Task<bool> TryCreateIdentityConnectionClient(string dotYouId, ClientAuthenticationToken remoteIcrClientAuthToken, out ClientAccessToken clientAccessToken)
        {
            if (null == remoteIcrClientAuthToken)
            {
                clientAccessToken = null;
                return Task.FromResult(false);
            }

            var icr = this.GetIdentityConnectionRegistration(new DotYouIdentity(dotYouId), remoteIcrClientAuthToken).GetAwaiter().GetResult();

            if (!icr.IsConnected())
            {
                clientAccessToken = null;
                return Task.FromResult(false);
            }

            var (grantKeyStoreKey, ss) = icr.AccessGrant.AccessRegistration.DecryptUsingClientAuthenticationToken(remoteIcrClientAuthToken);
            var (accessRegistration, cat) = _exchangeGrantService.CreateClientAccessToken(grantKeyStoreKey, ClientTokenType.IdentityConnectionRegistration).GetAwaiter().GetResult();
            grantKeyStoreKey.Wipe();
            ss.Wipe();

            clientAccessToken = cat;

            var icrClient = new IdentityConnectionRegistrationClient()
            {
                Id = accessRegistration.Id,
                AccessRegistration = accessRegistration,
                DotYouId = (DotYouIdentity)dotYouId
            };

            _icrClientValueStorage.Upsert(accessRegistration.Id, Array.Empty<byte>(), _icrClientDataType, icrClient);

            return Task.FromResult(true);
        }


        private async Task HandleDriveUpdated(StorageDrive drive)
        {
            //examine system circle; remove drive if needed
            CircleDefinition systemCircle = this.GetCircleDefinition(CircleConstants.SystemCircleId);

            var existingDriveGrant = systemCircle.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == drive.TargetDriveInfo);
            if (drive.AllowAnonymousReads == false && existingDriveGrant != null)
            {
                //remove the drive as it no longer allows anonymous reads
                systemCircle.DriveGrants = systemCircle.DriveGrants.Where(dg => dg.PermissionedDrive.Drive != drive.TargetDriveInfo).ToList();
                await this.UpdateCircleDefinition(systemCircle);
                return;
            }

            if (drive.AllowAnonymousReads && null == existingDriveGrant)
            {
                //act like it's new
                await this.HandleDriveAdded(drive);
            }
        }


        /// <summary>
        /// Updates the system circle's drive grants
        /// </summary>
        private async Task HandleDriveAdded(StorageDrive drive)
        {
            //only add anonymous drives
            if (drive.AllowAnonymousReads == false)
            {
                return;
            }

            CircleDefinition def = this.GetCircleDefinition(CircleConstants.SystemCircleId);

            var grants = def.DriveGrants.ToList();
            grants.Add(new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = drive.TargetDriveInfo,
                    Permission = DrivePermission.Read
                }
            });

            def.DriveGrants = grants;
            await this.UpdateCircleDefinition(def);
        }

        public Task Handle(DriveDefinitionAddedNotification notification, CancellationToken cancellationToken)
        {
            if (notification.IsNewDrive)
            {
                this.HandleDriveAdded(notification.Drive).GetAwaiter().GetResult();
            }
            else
            {
                this.HandleDriveUpdated(notification.Drive).GetAwaiter().GetResult();
            }

            return Task.CompletedTask;
        }

        //

        private async Task<CircleGrant> CreateCircleGrant(CircleDefinition def, SensitiveByteArray keyStoreKey, SensitiveByteArray masterKey)
        {
            //map the exchange grant to a structure that matches ICR
            var grant = await _exchangeGrantService.CreateExchangeGrant(keyStoreKey, def.Permissions, def.DriveGrants, masterKey);
            return new CircleGrant()
            {
                CircleId = def.Id,
                KeyStoreKeyEncryptedDriveGrants = grant.KeyStoreKeyEncryptedDriveGrants,
                PermissionSet = grant.PermissionSet,
            };
        }

        private async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreatePermissionContextInternal(Dictionary<string, CircleGrant> circleGrants,
            AccessRegistration accessReg,
            ClientAuthenticationToken authToken)
        {
            // Note: remember that all connected users are added to a system
            // circle; this circle has grants to all drives marked allowAnonymous == true
            var grants = new Dictionary<string, ExchangeGrant>();
            var enabledCircles = new List<GuidId>();
            foreach (var kvp in circleGrants)
            {
                var cg = kvp.Value;
                if (_circleDefinitionService.IsEnabled(cg.CircleId))
                {
                    enabledCircles.Add(cg.CircleId);
                    grants.Add(kvp.Key, new ExchangeGrant()
                    {
                        Created = 0,
                        Modified = 0,
                        IsRevoked = false, //TODO
                        KeyStoreKeyEncryptedDriveGrants = cg.KeyStoreKeyEncryptedDriveGrants,
                        MasterKeyEncryptedKeyStoreKey = null, //not required since this is not being created for the owner
                        PermissionSet = cg.PermissionSet
                    });
                }
            }

            List<int> permissionKeys = new List<int>() { };
            if (_tenantContext.TenantSystemConfig?.AllConnectedIdentitiesCanViewConnections ?? false)
            {
                permissionKeys.Add(PermissionKeys.ReadConnections);
            }

            var permissionCtx = await _exchangeGrantService.CreatePermissionContext(authToken, grants, accessReg, isOwner: false, additionalPermissionKeys: permissionKeys);
            return (permissionCtx, enabledCircles);
        }


        private async Task<PagedResult<IdentityConnectionRegistration>> GetConnectionsInternal(PageOptions req, ConnectionStatus status)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);

            var list = _storage.GetList().Where(icr => icr.Status == status);
            return new PagedResult<IdentityConnectionRegistration>(req, 1, list.ToList());
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

        private void SaveIcr(IdentityConnectionRegistration icr)
        {
            if (icr.Status == ConnectionStatus.None)
            {
                _circleMemberStorage.DeleteMembers(new List<byte[]>() { icr.DotYouId.ToByteArray() });
            }

            //TODO: this is a critical change; need to audit this
            _storage.Upsert(icr);
        }
    }
}