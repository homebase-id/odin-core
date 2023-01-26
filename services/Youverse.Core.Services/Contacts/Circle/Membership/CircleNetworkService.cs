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
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Notification;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.IdentityDatabase;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// <inheritdoc cref="ICircleNetworkService"/>
    /// </summary>
    public class CircleNetworkService : ICircleNetworkService, INotificationHandler<DriveDefinitionAddedNotification>, INotificationHandler<AppRegistrationChangedNotification>
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly CircleNetworkStorage _storage;
        private readonly CircleDefinitionService _circleDefinitionService;
        private readonly TableCircleMember _circleMemberStorage;
        private readonly TenantContext _tenantContext;
        private readonly IMediator _mediator;
        private readonly IAppRegistrationService _appRegistrationService;

        private readonly GuidId _icrClientDataType = GuidId.FromString("__icr_client_reg");
        private readonly ThreeKeyValueStorage _icrClientValueStorage;

        public CircleNetworkService(DotYouContextAccessor contextAccessor, ILogger<ICircleNetworkService> logger, ITenantSystemStorage tenantSystemStorage,
            IDotYouHttpClientFactory dotYouHttpClientFactory, ExchangeGrantService exchangeGrantService, TenantContext tenantContext, CircleDefinitionService circleDefinitionService,
            IMediator mediator, IAppRegistrationService appRegistrationService)
        {
            _contextAccessor = contextAccessor;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _circleDefinitionService = circleDefinitionService;
            _mediator = mediator;
            _appRegistrationService = appRegistrationService;

            _storage = new CircleNetworkStorage(tenantContext.StorageConfig.DataStoragePath);

            _circleMemberStorage = tenantSystemStorage.CircleMemberStorage;
            _icrClientValueStorage = tenantSystemStorage.IcrClientStorage;
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
                throw new YouverseClientException("Invalid notification type", YouverseClientErrorCode.InvalidNotificationType);
            }

            //TODO: thse should go into a background queue for processing offline.
            // processing them here means they're going to be called using the senderDI's context

            throw new YouverseClientException($"Unknown notification Id {notification.NotificationId}", YouverseClientErrorCode.UnknownNotificationId);
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

            var (permissionContext, enabledCircles) = await CreatePermissionContextInternal(icr, authToken, applyAppCircleGrants: true);
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
                var (permissionContext, enabledCircles) = await CreatePermissionContextInternal(icr, authToken);
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
                if (_tenantContext.Settings.AuthenticatedIdentitiesCanViewConnections)
                {
                    permissionKeys.Add(PermissionKeys.ReadConnections);
                }

                if (_tenantContext.Settings.AuthenticatedIdentitiesCanViewWhoIFollow)
                {
                    permissionKeys.Add(PermissionKeys.ReadWhoIFollow);
                }

                //create permission context with anonymous drives only
                var anonPermissionContext = await _exchangeGrantService.CreatePermissionContext(
                    authToken: authToken,
                    grants: null,
                    accessReg: icr.AccessGrant.AccessRegistration,
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
                throw new YouverseClientException($"{dotYouId} is already member of circle", YouverseClientErrorCode.IdentityAlreadyMemberOfCircle);
            }

            var circleDefinition = _circleDefinitionService.GetCircle(circleId);
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
            var circleGrant = await this.CreateCircleGrant(circleDefinition, keyStoreKey, masterKey);

            icr.AccessGrant.CircleGrants.Add(circleGrant.CircleId.ToBase64(), circleGrant);

            //
            // Check the apps.  If the circle being granted is authorized by an app
            // ensure the new member gets the permissions given by the app
            //
            var allApps = await _appRegistrationService.GetRegisteredApps();
            var appsThatGrantThisCircle = allApps.Where(reg => reg?.AuthorizedCircles?.Any(c => c == circleId) ?? false);

            foreach (var app in appsThatGrantThisCircle)
            {
                var appKey = app.AppId.Value.ToString();
                //ensure the circle is granted to the identity
                var appCircleGrant = await this.CreateAppCircleGrant(app, circleId, keyStoreKey, masterKey);

                if (!icr.AccessGrant.AppGrants.Remove(appKey, out var appCircleGrantsDictionary))
                {
                    appCircleGrantsDictionary = new();
                }

                appCircleGrantsDictionary[circleId.Value.ToString()] = appCircleGrant;
                icr.AccessGrant.AppGrants[appKey] = appCircleGrantsDictionary;
            }

            keyStoreKey.Wipe();

            _circleMemberStorage.AddMembers(circleId, new List<byte[]>() { dotYouId.ToByteArray() });
            this.SaveIcr(icr);
        }

        public async Task RevokeCircleAccess(GuidId circleId, DotYouIdentity dotYouId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var icr = await this.GetIdentityConnectionRegistrationInternal(dotYouId);
            if (icr.AccessGrant == null)
            {
                return;
            }

            var circle64 = circleId.ToBase64();
            if (icr.AccessGrant.CircleGrants.ContainsKey(circle64))
            {
                if (!icr.AccessGrant.CircleGrants.Remove(circleId.ToBase64()))
                {
                    throw new YouverseClientException($"Failed to remove {circle64} from {dotYouId}");
                }
            }

            //find the circle grant across all appsgrants and remove it
            foreach (var (appKey, appCircleGrants) in icr.AccessGrant.AppGrants)
            {
                appCircleGrants.Remove(circleId.Value.ToString());
            }


            _circleMemberStorage.RemoveMembers(circleId, new List<byte[]>() { dotYouId.ToByteArray() });
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

        public async Task<Dictionary<string, Dictionary<string, AppCircleGrant>>> CreateAppCircleGrantList(List<GuidId> circleIds, SensitiveByteArray keyStoreKey)
        {
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

            var allApps = await _appRegistrationService.GetRegisteredApps();
            var appGrants = new Dictionary<string, Dictionary<string, AppCircleGrant>>(StringComparer.Ordinal);

            foreach (var circleId in circleIds)
            {
                var appsThatGrantThisCircle = allApps.Where(reg => reg?.AuthorizedCircles?.Any(c => c == circleId) ?? false);

                foreach (var app in appsThatGrantThisCircle)
                {
                    var appKey = app.AppId.Value.ToString();
                    var appCircleGrant = await this.CreateAppCircleGrant(app, circleId, keyStoreKey, masterKey);

                    if (!appGrants.TryGetValue(appKey, out var appCircleGrantsDictionary))
                    {
                        appCircleGrantsDictionary = new Dictionary<string, AppCircleGrant>(StringComparer.Ordinal);
                    }

                    appCircleGrantsDictionary[circleId.Value.ToString()] = appCircleGrant;
                    appGrants[appKey] = appCircleGrantsDictionary;
                }
            }

            return appGrants;
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

            // List<DotYouIdentity> invalidMembers = new List<DotYouIdentity>();
            foreach (var dotYouId in members)
            {
                var icr = await this.GetIdentityConnectionRegistrationInternal(dotYouId);

                var circleKey = circleDef.Id.ToBase64();
                var hasCg = icr.AccessGrant.CircleGrants.Remove(circleKey, out _);

                if (icr.IsConnected() && hasCg)
                {
                    //rebuild the circle grant
                    var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
                    icr.AccessGrant.CircleGrants[circleKey] = await this.CreateCircleGrant(circleDef, keyStoreKey, masterKey);
                    keyStoreKey.Wipe();
                }
                else
                {
                    //It should not occur that a circle has a member
                    //who is not connected but let's capture it
                    // invalidMembers.Add(dotYouId);
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
                throw new YouverseClientException("Cannot delete a circle with members", YouverseClientErrorCode.CannotDeleteCircleWithMembers);
            }

            await _circleDefinitionService.Delete(circleId);
        }

        public Task DisableCircle(GuidId circleId)
        {
            var circle = _circleDefinitionService.GetCircle(circleId);
            circle.Disabled = true;
            circle.LastUpdated = UnixTimeUtc.Now().milliseconds;
            _circleDefinitionService.Update(circle);
            return Task.CompletedTask;
        }

        public Task EnableCircle(GuidId circleId)
        {
            var circle = _circleDefinitionService.GetCircle(circleId);
            circle.Disabled = false;
            circle.LastUpdated = UnixTimeUtc.Now().milliseconds;
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

        public void Dispose()
        {
            _storage.Dispose();
        }

        public async Task Handle(AppRegistrationChangedNotification notification, CancellationToken cancellationToken)
        {
            await this.ReconcileAuthorizedCircles(notification.OldAppRegistration, notification.NewAppRegistration);
        }

        //

        private async Task<AppCircleGrant> CreateAppCircleGrant(RedactedAppRegistration appReg, GuidId circleId, SensitiveByteArray keyStoreKey, SensitiveByteArray masterKey)
        {
            //map the exchange grant to a structure that matches ICR
            var grant = await _exchangeGrantService.CreateExchangeGrant(keyStoreKey,
                appReg.CircleMemberPermissionSetGrantRequest.PermissionSet,
                appReg.CircleMemberPermissionSetGrantRequest.Drives,
                masterKey);

            return new AppCircleGrant()
            {
                AppId = appReg.AppId,
                CircleId = circleId,
                KeyStoreKeyEncryptedDriveGrants = grant.KeyStoreKeyEncryptedDriveGrants,
                PermissionSet = grant.PermissionSet,
            };
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

        private async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreatePermissionContextInternal(
            IdentityConnectionRegistration connectionRegistration,
            ClientAuthenticationToken authToken,
            bool applyAppCircleGrants = false)
        {
            //TODO: this code needs to be refactored to avoid all the mapping

            //Map CircleGrants and AppCircleGrants to Exchange grants
            // Note: remember that all connected users are added to a system
            // circle; this circle has grants to all drives marked allowAnonymous == true
            var grants = new Dictionary<string, ExchangeGrant>();
            var enabledCircles = new List<GuidId>();
            foreach (var kvp in connectionRegistration.AccessGrant.CircleGrants)
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

            if (applyAppCircleGrants)
            {
                foreach (var kvp in connectionRegistration.AccessGrant.AppGrants)
                {
                    var appId = kvp.Key;
                    var appCircleGrantDictionary = kvp.Value;

                    foreach (var (circleId, appCg) in appCircleGrantDictionary)
                    {
                        var alreadyEnabledCircle = enabledCircles.Exists(cid => cid == appCg.CircleId);
                        if (alreadyEnabledCircle || _circleDefinitionService.IsEnabled(appCg.CircleId))
                        {
                            if (!alreadyEnabledCircle)
                            {
                                enabledCircles.Add(appCg.CircleId);
                            }

                            grants.Add(kvp.Key, new ExchangeGrant()
                            {
                                Created = 0,
                                Modified = 0,
                                IsRevoked = false, //TODO
                                KeyStoreKeyEncryptedDriveGrants = appCg.KeyStoreKeyEncryptedDriveGrants,
                                MasterKeyEncryptedKeyStoreKey = null, //not required since this is not being created for the owner
                                PermissionSet = appCg.PermissionSet
                            });
                        }
                    }
                }
            }

            List<int> permissionKeys = new List<int>() { };
            if (_tenantContext.Settings?.AllConnectedIdentitiesCanViewConnections ?? false)
            {
                permissionKeys.Add(PermissionKeys.ReadConnections);
            }

            if (_tenantContext.Settings?.AllConnectedIdentitiesCanViewWhoIFollow ?? false)
            {
                permissionKeys.Add(PermissionKeys.ReadWhoIFollow);
            }

            var permissionCtx = await _exchangeGrantService.CreatePermissionContext(authToken, grants, connectionRegistration.AccessGrant.AccessRegistration, additionalPermissionKeys: permissionKeys);
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

            //notify anyone caching data for this identity, we need to reset the cache
            // _mediator.Publish(new IdentityConnectionRegistrationChangedNotification()
            // {
            //     DotYouId = icr.DotYouId
            // });
        }

        private async Task ReconcileAuthorizedCircles(AppRegistration oldAppRegistration, AppRegistration newAppRegistration)
        {
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var appKey = newAppRegistration.AppId.Value.ToString();

            //TODO: use _db.CreateCommitUnitOfWork()
            if (null != oldAppRegistration)
            {
                var circlesToRevoke = oldAppRegistration.AuthorizedCircles.Except(newAppRegistration.AuthorizedCircles);
                //TODO: spin thru circles to revoke an update members

                foreach (var circleId in circlesToRevoke)
                {
                    //get all circle members and update their grants
                    var members = await this.GetCircleMembers(circleId);

                    foreach (var dotYouId in members)
                    {
                        var icr = await this.GetIdentityConnectionRegistrationInternal(dotYouId);
                        var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
                        icr.AccessGrant.AppGrants[appKey]?.Remove(circleId.ToString());
                        keyStoreKey.Wipe();
                        this.SaveIcr(icr);
                    }
                }
            }

            foreach (var circleId in newAppRegistration.AuthorizedCircles ?? new List<Guid>())
            {
                //get all circle members and update their grants
                var members = await this.GetCircleMembers(circleId);

                foreach (var dotYouId in members)
                {
                    var icr = await this.GetIdentityConnectionRegistrationInternal(dotYouId);
                    var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);

                    var appCircleGrant = await this.CreateAppCircleGrant(newAppRegistration.Redacted(), circleId, keyStoreKey, masterKey);

                    if (!icr.AccessGrant.AppGrants.TryGetValue(appKey, out var appCircleGrantDictionary))
                    {
                        appCircleGrantDictionary = new Dictionary<string, AppCircleGrant>();
                    }

                    appCircleGrantDictionary[appCircleGrant.CircleId.ToString()] = appCircleGrant;
                    icr.AccessGrant.AppGrants[appKey] = appCircleGrantDictionary;

                    keyStoreKey.Wipe();

                    this.SaveIcr(icr);
                }
            }


            //
        }
    }
}