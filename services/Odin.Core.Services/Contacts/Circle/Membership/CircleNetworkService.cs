using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Contacts.Circle.Membership.Definition;
using Odin.Core.Services.Contacts.Circle.Requests;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Mediator;
using Odin.Core.Storage;
using Odin.Core.Time;
using PermissionSet = Odin.Core.Services.Authorization.Permissions.PermissionSet;

namespace Odin.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public class CircleNetworkService : INotificationHandler<DriveDefinitionAddedNotification>,
        INotificationHandler<AppRegistrationChangedNotification>
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly CircleNetworkStorage _storage;
        private readonly CircleDefinitionService _circleDefinitionService;
        private readonly TenantContext _tenantContext;
        private readonly IAppRegistrationService _appRegistrationService;
        private readonly GuidId _icrClientDataType = GuidId.FromString("__icr_client_reg");
        private readonly ThreeKeyValueStorage _icrClientValueStorage;

        public CircleNetworkService(OdinContextAccessor contextAccessor,
            ExchangeGrantService exchangeGrantService, TenantContext tenantContext,
            CircleDefinitionService circleDefinitionService,
            IAppRegistrationService appRegistrationService, TenantSystemStorage tenantSystemStorage)
        {
            _contextAccessor = contextAccessor;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _circleDefinitionService = circleDefinitionService;
            _appRegistrationService = appRegistrationService;

            _storage = new CircleNetworkStorage(tenantSystemStorage);

            _icrClientValueStorage = tenantSystemStorage.IcrClientStorage;
        }

        /// <summary>
        /// Creates a <see cref="PermissionContext"/> for the specified caller based on their access
        /// </summary>
        public async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreateTransitPermissionContext(OdinId odinId,
            ClientAuthenticationToken authToken)
        {
            var icr = await this.GetIdentityConnectionRegistration(odinId, authToken);

            if (!icr.AccessGrant?.IsValid() ?? false)
            {
                throw new OdinSecurityException("Invalid token");
            }

            if (!icr.IsConnected())
            {
                throw new OdinSecurityException("Invalid connection");
            }

            var (permissionContext, enabledCircles) = await CreatePermissionContextInternal(
                connectionRegistration: icr,
                authToken: authToken,
                accessReg: icr.AccessGrant!.AccessRegistration,
                applyAppCircleGrants: true);

            return (permissionContext, enabledCircles);
        }

        /// <summary>
        /// Creates a caller and permission context for the caller based on the <see cref="IdentityConnectionRegistrationClient"/> resolved by the authToken
        /// </summary>
        public async Task<(CallerContext callerContext, PermissionContext permissionContext)> CreateConnectedYouAuthClientContext(
            ClientAuthenticationToken authToken)
        {
            var client = await this.GetIdentityConnectionClient(authToken);
            if (null == client)
            {
                throw new OdinSecurityException("Invalid token");
            }

            client.AccessRegistration.AssertValidRemoteKey(authToken.AccessTokenHalfKey);

            var icr = await this.GetIdentityConnectionRegistrationInternal(client.OdinId);
            bool isAuthenticated = icr.AccessGrant?.IsValid() ?? false;
            bool isConnected = icr.IsConnected();

            // Only return the permissions if the identity is connected.
            if (isAuthenticated && isConnected)
            {
                var (permissionContext, enabledCircles) = await CreatePermissionContextInternal(
                    connectionRegistration: icr,
                    accessReg: client.AccessRegistration,
                    authToken: authToken);

                var cc = new CallerContext(
                    odinId: client.OdinId,
                    masterKey: null,
                    securityLevel: SecurityGroupType.Connected,
                    circleIds: enabledCircles);

                return (cc, permissionContext);
            }

            // Otherwise, fall back to anonymous drives
            if (isAuthenticated)
            {
                var cc = new CallerContext(
                    odinId: client.OdinId,
                    masterKey: null,
                    securityLevel: SecurityGroupType.Authenticated);

                List<int> permissionKeys = new List<int>();
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
                    grants: null!,
                    accessReg: icr.AccessGrant.AccessRegistration,
                    additionalPermissionKeys: permissionKeys);

                return (cc, anonPermissionContext);
            }

            throw new OdinSecurityException("Invalid auth token");
        }

        /// <summary>
        /// Disconnects you from the specified <see cref="OdinId"/>
        /// </summary>
        /// <param name="odinId"></param>
        /// <returns></returns>
        public async Task<bool> Disconnect(OdinId odinId)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetIdentityConnectionRegistration(odinId);
            if (info is { Status: ConnectionStatus.Connected })
            {
                _storage.Delete(odinId);

                //destroy all access
                // info.AccessGrant = null;
                //TODO: remove ICR clients
                // _icrClientValueStorage.Delete();
                // info.Status = ConnectionStatus.None;
                // this.SaveIcr(info);

                return true;
            }


            return false;
        }

        /// <summary>
        /// Blocks the specified <see cref="OdinId"/> from your network
        /// </summary>
        /// <param name="odinId"></param>
        /// <returns></returns>
        public async Task<bool> Block(OdinId odinId)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetIdentityConnectionRegistration(odinId);

            //TODO: when you block a connection, you must also destroy exchange grant

            if (null != info && info.Status == ConnectionStatus.Connected)
            {
                info.Status = ConnectionStatus.Blocked;
                this.SaveIcr(info);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets profiles that have been marked as <see cref="ConnectionStatus.Blocked"/>
        /// </summary>
        public async Task<CursoredResult<long, IdentityConnectionRegistration>> GetBlockedProfiles(int count, long cursor)
        {
            return await Task.FromResult(this.GetConnectionsInternal(count, cursor, ConnectionStatus.Blocked));
        }

        /// <summary>
        /// Returns a list of identities which are connected to this DI
        /// </summary>
        public async Task<CursoredResult<long, IdentityConnectionRegistration>> GetConnectedIdentities(int count, long cursor)
        {
            return await Task.FromResult(this.GetConnectionsInternal(count, cursor, ConnectionStatus.Connected));
        }

        /// <summary>
        /// Unblocks the specified <see cref="OdinId"/> from your network
        /// </summary>
        /// <param name="odinId"></param>
        /// <returns></returns>
        public async Task<bool> Unblock(OdinId odinId)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetIdentityConnectionRegistration(odinId);
            if (null != info && info.Status == ConnectionStatus.Blocked)
            {
                info.Status = ConnectionStatus.Connected;
                this.SaveIcr(info);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the current connection info
        /// </summary>
        /// <param name="odinId"></param>
        /// <param name="overrideHack"></param>
        /// <returns></returns>
        public async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistration(OdinId odinId, bool overrideHack = false)
        {
            //TODO: need to cache here?
            //HACK: DOING THIS WHILE DESIGNING XTOKEN - REMOVE THIS
            if (!overrideHack)
            {
                _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
            }

            return await GetIdentityConnectionRegistrationInternal(odinId);
        }

        /// <summary>
        /// Gets the connection info if the specified <param name="remoteClientAuthenticationToken">xtoken half key</param> is valid
        /// </summary>
        /// <param name="odinId"></param>
        /// <param name="remoteClientAuthenticationToken"></param>
        /// <returns></returns>
        public async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistration(OdinId odinId,
            ClientAuthenticationToken remoteClientAuthenticationToken)
        {
            var connection = await GetIdentityConnectionRegistrationInternal(odinId);

            if (connection?.AccessGrant?.AccessRegistration == null)
            {
                throw new OdinSecurityException("Unauthorized Action");
            }

            connection.AccessGrant.AccessRegistration.AssertValidRemoteKey(remoteClientAuthenticationToken.AccessTokenHalfKey);

            return connection;
        }

        /// <summary>
        /// Gets the access registration granted to the <param name="odinId"></param>
        /// </summary>
        /// <param name="odinId"></param>
        /// <param name="remoteIdentityConnectionKey"></param>
        /// <returns></returns>
        public async Task<AccessRegistration> GetIdentityConnectionAccessRegistration(OdinId odinId, SensitiveByteArray remoteIdentityConnectionKey)
        {
            var connection = await GetIdentityConnectionRegistrationInternal(odinId);

            if (connection?.AccessGrant.AccessRegistration == null || connection.IsConnected() == false)
            {
                throw new OdinSecurityException("Unauthorized Action");
            }

            connection.AccessGrant.AccessRegistration.AssertValidRemoteKey(remoteIdentityConnectionKey);

            return connection.AccessGrant.AccessRegistration;
        }

        /// <summary>
        /// Determines if the specified odinId is connected 
        /// </summary>
        /// <param name="odinId"></param>
        /// <returns></returns>
        public async Task<bool> IsConnected(OdinId odinId)
        {
            //allow the caller to see if s/he is connected, otherwise
            if (_contextAccessor.GetCurrent().Caller.OdinId != odinId)
            {
                //TODO: this needs to be changed to - can view connections
                _contextAccessor.GetCurrent().AssertCanManageConnections();
            }

            var info = await this.GetIdentityConnectionRegistration(odinId);
            return info.Status == ConnectionStatus.Connected;
        }

        public async Task<IEnumerable<OdinId>> GetCircleMembers(GuidId circleId)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
            var result = _storage.GetCircleMembers(circleId);
            return await Task.FromResult(result);
        }

        /// <summary>
        /// Throws an exception if the odinId is blocked.
        /// </summary>
        /// <param name="odinId"></param>
        /// <returns></returns>
        public async Task AssertConnectionIsNoneOrValid(OdinId odinId)
        {
            var info = await this.GetIdentityConnectionRegistration(odinId);
            this.AssertConnectionIsNoneOrValid(info);
        }

        /// <summary>
        /// Throws an exception if the odinId is blocked.
        /// </summary>
        /// <param name="registration">The connection info to be checked</param>
        /// <returns></returns>
        public void AssertConnectionIsNoneOrValid(IdentityConnectionRegistration registration)
        {
            if (registration.Status == ConnectionStatus.Blocked)
            {
                throw new SecurityException("OdinId is blocked");
            }
        }

        /// <summary>
        /// Adds the specified odinId to your network
        /// </summary>
        /// <param name="odinIdentity">The public key certificate containing the domain name which will be connected</param>
        /// <param name="accessGrant">The access to be given to this connection</param>
        /// <param name="encryptedCat">The keys used when accessing the remote identity</param>
        /// <param name="contactData"></param>
        /// <returns></returns>
        public async Task Connect(string odinIdentity, AccessExchangeGrant accessGrant, EncryptedClientAccessToken encryptedCat, ContactRequestData contactData)
        {
            //TODO: need to add security that this method can be called

            if (encryptedCat == null || encryptedCat.EncryptedData.KeyEncrypted.Length == 0)
            {
                throw new OdinSecurityException("Invalid EncryptedClientAccessToken");
            }

            var odinId = (OdinId)odinIdentity;

            //1. validate current connection state
            var info = await this.GetIdentityConnectionRegistrationInternal(odinId);

            if (info.Status != ConnectionStatus.None)
            {
                throw new OdinSecurityException("invalid connection state");
            }

            //TODO: need to scan the YouAuthService to see if this user has a YouAuthRegistration

            //2. add the record to the list of connections
            var newConnection = new IdentityConnectionRegistration()
            {
                OdinId = odinId,
                Status = ConnectionStatus.Connected,
                Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                OriginalContactData = contactData,
                AccessGrant = accessGrant,

                // ClientAccessTokenId = remoteClientAccessToken.Id,
                // ClientAccessTokenHalfKey = remoteClientAccessToken.AccessTokenHalfKey.GetKey(),
                // ClientAccessTokenSharedSecret = remoteClientAccessToken.SharedSecret.GetKey(),

                EncryptedClientAccessToken = encryptedCat
            };

            this.SaveIcr(newConnection);
        }

        /// <summary>
        /// Gives access to all resource granted by the specified circle to the odinId
        /// </summary>
        public async Task GrantCircle(GuidId circleId, OdinId odinId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var icr = await this.GetIdentityConnectionRegistrationInternal(odinId);

            if (icr == null || !icr.IsConnected())
            {
                throw new OdinSecurityException($"{odinId} must have valid connection to be added to a circle");
            }

            if (icr.AccessGrant.CircleGrants.TryGetValue(circleId, out var _))
            {
                //TODO: Here we should ensure it's in the _circleMemberStorage just in case this was called because it's out of sync
                throw new OdinClientException($"{odinId} is already member of circle", OdinClientErrorCode.IdentityAlreadyMemberOfCircle);
            }

            var circleDefinition = _circleDefinitionService.GetCircle(circleId);
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
            var circleGrant = await this.CreateCircleGrant(circleDefinition, keyStoreKey, masterKey);

            icr.AccessGrant.CircleGrants.Add(circleGrant.CircleId, circleGrant);

            //
            // Check the apps.  If the circle being granted is authorized by an app
            // ensure the new member gets the permissions given by the app
            //
            var allApps = await _appRegistrationService.GetRegisteredApps();
            var appsThatGrantThisCircle = allApps.Where(reg => reg?.AuthorizedCircles?.Any(c => c == circleId) ?? false);

            foreach (var app in appsThatGrantThisCircle)
            {
                var appCircleGrant = await this.CreateAppCircleGrant(app, circleId, keyStoreKey, masterKey);
                icr.AccessGrant.AddUpdateAppCircleGrant(appCircleGrant);
            }

            keyStoreKey.Wipe();
            this.SaveIcr(icr);
        }

        /// <summary>
        /// Removes drives and permissions of the specified circle from the odinId
        /// </summary>
        public async Task RevokeCircleAccess(GuidId circleId, OdinId odinId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var icr = await this.GetIdentityConnectionRegistrationInternal(odinId);
            if (icr.AccessGrant == null)
            {
                return;
            }

            if (icr.AccessGrant.CircleGrants.ContainsKey(circleId))
            {
                if (!icr.AccessGrant.CircleGrants.Remove(circleId))
                {
                    throw new OdinClientException($"Failed to remove {circleId} from {odinId}");
                }
            }

            //find the circle grant across all app grants and remove it
            foreach (var (appKey, appCircleGrants) in icr.AccessGrant.AppGrants)
            {
                appCircleGrants.Remove(circleId.Value);
            }

            this.SaveIcr(icr);
        }

        public async Task<Dictionary<Guid, CircleGrant>> CreateCircleGrantList(List<GuidId> circleIds, SensitiveByteArray keyStoreKey)
        {
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

            var circleGrants = new Dictionary<Guid, CircleGrant>();

            // Always put identities in the system circle
            var cids = circleIds ?? new List<GuidId>();
            cids.Add(CircleConstants.SystemCircleId);

            foreach (var id in cids ?? new List<GuidId>())
            {
                var def = _circleDefinitionService.GetCircle(id);

                var cg = await this.CreateCircleGrant(def, keyStoreKey, masterKey);
                circleGrants.Add(id.Value, cg);
            }

            return circleGrants;
        }

        public async Task<Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>> CreateAppCircleGrantList(List<GuidId> circleIds,
            SensitiveByteArray keyStoreKey)
        {
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

            var allApps = await _appRegistrationService.GetRegisteredApps();
            var appGrants = new Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>();

            foreach (var circleId in circleIds)
            {
                var appsThatGrantThisCircle = allApps.Where(reg => reg?.AuthorizedCircles?.Any(c => c == circleId) ?? false);

                foreach (var app in appsThatGrantThisCircle ?? new List<RedactedAppRegistration>())
                {
                    var appKey = app.AppId.Value;
                    var appCircleGrant = await this.CreateAppCircleGrant(app, circleId, keyStoreKey, masterKey);

                    if (!appGrants.TryGetValue(appKey, out var appCircleGrantsDictionary))
                    {
                        appCircleGrantsDictionary = new Dictionary<Guid, AppCircleGrant>();
                    }

                    appCircleGrantsDictionary[circleId.Value] = appCircleGrant;
                    appGrants[appKey] = appCircleGrantsDictionary;
                }
            }

            return appGrants;
        }

        /// <summary>
        /// Gets a circle definition
        /// </summary>
        /// <param name="circleId"></param>
        public CircleDefinition GetCircleDefinition(GuidId circleId)
        {
            Guard.Argument(circleId, nameof(circleId)).NotNull().Require(id => GuidId.IsValid(id));
            var def = _circleDefinitionService.GetCircle(circleId);
            return def;
        }

        /// <summary>
        /// Gets a list of all circle definitions
        /// </summary>
        public async Task<IEnumerable<CircleDefinition>> GetCircleDefinitions(bool includeSystemCircle)
        {
            var circles = await _circleDefinitionService.GetCircles(includeSystemCircle);
            return circles;
        }

        /// <summary>
        /// Creates a circle definition
        /// </summary>
        /// <param name="request"></param>
        public async Task CreateCircleDefinition(CreateCircleRequest request)
        {
            await _circleDefinitionService.Create(request);
        }

        /// <summary>
        /// Updates a <see cref="CircleDefinition"/> and applies permission and drive changes to all existing circle members
        /// </summary>
        /// <param name="circleDefinition"></param>
        public async Task UpdateCircleDefinition(CircleDefinition circleDef)
        {
            Guard.Argument(circleDef, nameof(circleDef)).NotNull();

            _circleDefinitionService.AssertValidDriveGrants(circleDef.DriveGrants);

            var members = await GetCircleMembers(circleDef.Id);
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();

            // List<OdinId> invalidMembers = new List<OdinId>();
            foreach (var odinId in members)
            {
                var icr = await this.GetIdentityConnectionRegistrationInternal(odinId);

                var circleKey = circleDef.Id;
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
                    // invalidMembers.Add(odinId);
                }

                this.SaveIcr(icr);
            }

            await _circleDefinitionService.Update(circleDef);

            //TODO: determine how to handle invalidMembers - do we return to the UI?  do we remove from all circles?
        }

        /// <summary>
        /// Tests if a circle has members and indicates if it can be deleted
        /// </summary>
        public async Task DeleteCircleDefinition(GuidId circleId)
        {
            var members = await this.GetCircleMembers(circleId);

            if (members.Any())
            {
                throw new OdinClientException("Cannot delete a circle with members", OdinClientErrorCode.CannotDeleteCircleWithMembers);
            }

            await _circleDefinitionService.Delete(circleId);
        }

        /// <summary>
        /// Disables a circle without removing it.  The grants provided by the circle will not be available to the members
        /// </summary>
        /// <param name="circleId"></param>
        public Task DisableCircle(GuidId circleId)
        {
            var circle = _circleDefinitionService.GetCircle(circleId);
            circle.Disabled = true;
            circle.LastUpdated = UnixTimeUtc.Now().milliseconds;
            _circleDefinitionService.Update(circle);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Enables a circle
        /// </summary>
        /// <param name="circleId"></param>
        public Task EnableCircle(GuidId circleId)
        {
            var circle = _circleDefinitionService.GetCircle(circleId);
            circle.Disabled = false;
            circle.LastUpdated = UnixTimeUtc.Now().milliseconds;
            _circleDefinitionService.Update(circle);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns the <see cref="IdentityConnectionRegistrationClient"/> 
        /// </summary>
        /// <param name="authToken"></param>
        /// <returns></returns>
        public Task<IdentityConnectionRegistrationClient> GetIdentityConnectionClient(ClientAuthenticationToken authToken)
        {
            var client = _icrClientValueStorage.Get<IdentityConnectionRegistrationClient>(authToken.Id);
            return Task.FromResult(client);
        }

        /// <summary>
        /// Creates the system circle
        /// </summary>
        /// <returns></returns>
        public Task CreateSystemCircle()
        {
            _circleDefinitionService.CreateSystemCircle();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Creates a client for the IdentityConnectionRegistration
        /// </summary>
        /// <returns></returns>
        public Task<bool> TryCreateIdentityConnectionClient(string odinId, ClientAuthenticationToken remoteIcrClientAuthToken,
            out ClientAccessToken clientAccessToken)
        {
            if (null == remoteIcrClientAuthToken)
            {
                clientAccessToken = null;
                return Task.FromResult(false);
            }

            var icr = this.GetIdentityConnectionRegistration(new OdinId(odinId), remoteIcrClientAuthToken).GetAwaiter().GetResult();

            if (!icr.IsConnected())
            {
                clientAccessToken = null;
                return Task.FromResult(false);
            }

            var (grantKeyStoreKey, ss) = icr.AccessGrant.AccessRegistration.DecryptUsingClientAuthenticationToken(remoteIcrClientAuthToken);
            var (accessRegistration, cat) = _exchangeGrantService.CreateClientAccessToken(grantKeyStoreKey, ClientTokenType.IdentityConnectionRegistration)
                .GetAwaiter().GetResult();
            grantKeyStoreKey.Wipe();
            ss.Wipe();

            clientAccessToken = cat;

            var icrClient = new IdentityConnectionRegistrationClient()
            {
                Id = accessRegistration.Id,
                AccessRegistration = accessRegistration,
                OdinId = (OdinId)odinId
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

        public async Task Handle(AppRegistrationChangedNotification notification, CancellationToken cancellationToken)
        {
            await this.ReconcileAuthorizedCircles(notification.OldAppRegistration, notification.NewAppRegistration);
        }

        /// <summary>
        /// Creates initial encryption keys
        /// </summary>
        public async Task CreateInitialKeys()
        {
            var mk = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            _storage.CreateIcrKey(mk);
            await Task.CompletedTask;
        }

        public SymmetricKeyEncryptedAes ReEncryptIcrKey(SensitiveByteArray encryptionKey)
        {
            var rawIcrKey = GetRawIcrKey();
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(ref encryptionKey, ref rawIcrKey);
            rawIcrKey.Wipe();
            return encryptedIcrKey;
        }

        public EncryptedClientAccessToken EncryptClientAccessToken(ClientAccessToken clientAccessToken)
        {
            var rawIcrKey = GetRawIcrKey();
            var k = EncryptedClientAccessToken.Encrypt(rawIcrKey, clientAccessToken);
            rawIcrKey.Wipe();
            return k;
        }

        //

        private SensitiveByteArray GetRawIcrKey()
        {
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var masterKeyEncryptedIcrKey = _storage.GetMasterKeyEncryptedIcrKey();
            return masterKeyEncryptedIcrKey.DecryptKeyClone(ref masterKey);
        }

        private async Task<AppCircleGrant> CreateAppCircleGrant(RedactedAppRegistration appReg, GuidId circleId, SensitiveByteArray keyStoreKey,
            SensitiveByteArray masterKey)
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
            AccessRegistration accessReg,
            bool applyAppCircleGrants = false)
        {
            //TODO: this code needs to be refactored to avoid all the mapping

            //Map CircleGrants and AppCircleGrants to Exchange grants
            // Note: remember that all connected users are added to a system
            // circle; this circle has grants to all drives marked allowAnonymous == true
            var grants = new Dictionary<Guid, ExchangeGrant>();
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

            //Add write-ability to drives if this identity follows me
            // var follower = await _followerService.GetFollower(connectionRegistration.OdinId);
            // if (null != follower)
            // {
            //     if (follower.NotificationType == FollowerNotificationType.AllNotifications)
            //     {
            //         //get all drives that allow subscriptions of type channel
            //         
            //     }
            //
            //     if (follower.NotificationType == FollowerNotificationType.SelectedChannels)
            //     {
            //         follower.Channels
            //     }
            // }

            //TODO: only add this if I follow this identity
            var feedDriveWriteGrant = await _exchangeGrantService.CreateExchangeGrant(new PermissionSet(), new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new()
                    {
                        Drive = SystemDriveConstants.FeedDrive,
                        Permission = DrivePermission.Write
                    }
                }
            }, null);

            grants.Add(ByteArrayUtil.ReduceSHA256Hash("feed_drive_writer"), feedDriveWriteGrant);

            List<int> permissionKeys = new List<int>() { };
            if (_tenantContext.Settings?.AllConnectedIdentitiesCanViewConnections ?? false)
            {
                permissionKeys.Add(PermissionKeys.ReadConnections);
            }

            if (_tenantContext.Settings?.AllConnectedIdentitiesCanViewWhoIFollow ?? false)
            {
                permissionKeys.Add(PermissionKeys.ReadWhoIFollow);
            }

            var permissionCtx = await _exchangeGrantService.CreatePermissionContext(
                authToken: authToken,
                grants: grants,
                accessReg: accessReg,
                additionalPermissionKeys: permissionKeys);

            var result = (permissionCtx, enabledCircles);
            return await Task.FromResult(result);
        }


        private CursoredResult<long, IdentityConnectionRegistration> GetConnectionsInternal(int count, long cursor, ConnectionStatus status)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);

            var list = _storage.GetList(count, new UnixTimeUtcUnique(cursor), out var nextCursor, status);
            return new CursoredResult<long, IdentityConnectionRegistration>()
            {
                Cursor = nextCursor.GetValueOrDefault().uniqueTime,
                Results = list
            };
        }


        private async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistrationInternal(OdinId odinId)
        {
            var registration = _storage.Get(odinId);

            //registration.ClientAccessTokenSharedSecret

            if (null == registration)
            {
                return new IdentityConnectionRegistration()
                {
                    OdinId = odinId,
                    Status = ConnectionStatus.None,
                    LastUpdated = -1
                };
            }

            return await Task.FromResult(registration);
        }

        private void SaveIcr(IdentityConnectionRegistration icr)
        {
            //TODO: this is a critical change; need to audit this
            if (icr.Status == ConnectionStatus.None)
            {
                _storage.Delete(icr.OdinId);
            }
            else
            {
                _storage.Upsert(icr);
            }

            //notify anyone caching data for this identity, we need to reset the cache
            // _mediator.Publish(new IdentityConnectionRegistrationChangedNotification()
            // {
            //     OdinId = icr.OdinId
            // });
        }

        private async Task ReconcileAuthorizedCircles(AppRegistration oldAppRegistration, AppRegistration newAppRegistration)
        {
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var appKey = newAppRegistration.AppId.Value;

            //TODO: use _db.CreateCommitUnitOfWork()
            if (null != oldAppRegistration)
            {
                var circlesToRevoke = oldAppRegistration.AuthorizedCircles.Except(newAppRegistration.AuthorizedCircles);
                //TODO: spin thru circles to revoke an update members

                foreach (var circleId in circlesToRevoke)
                {
                    //get all circle members and update their grants
                    var members = await this.GetCircleMembers(circleId);

                    foreach (var odinId in members)
                    {
                        var icr = await this.GetIdentityConnectionRegistrationInternal(odinId);
                        var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
                        icr.AccessGrant.AppGrants[appKey]?.Remove(circleId);
                        keyStoreKey.Wipe();
                        this.SaveIcr(icr);
                    }
                }
            }

            foreach (var circleId in newAppRegistration.AuthorizedCircles ?? new List<Guid>())
            {
                //get all circle members and update their grants
                var members = await this.GetCircleMembers(circleId);

                foreach (var odinId in members)
                {
                    var icr = await this.GetIdentityConnectionRegistrationInternal(odinId);
                    var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);

                    var appCircleGrant = await this.CreateAppCircleGrant(newAppRegistration.Redacted(), circleId, keyStoreKey, masterKey);

                    if (!icr.AccessGrant.AppGrants.TryGetValue(appKey, out var appCircleGrantDictionary))
                    {
                        appCircleGrantDictionary = new Dictionary<Guid, AppCircleGrant>();
                    }

                    appCircleGrantDictionary[appCircleGrant.CircleId] = appCircleGrant;
                    icr.AccessGrant.AppGrants[appKey] = appCircleGrantDictionary;

                    keyStoreKey.Wipe();

                    this.SaveIcr(icr);
                }
            }
            //
        }
    }
}