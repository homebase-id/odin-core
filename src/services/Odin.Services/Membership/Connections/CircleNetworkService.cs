using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer.AppNotification;
using Permissions_PermissionSet = Odin.Services.Authorization.Permissions.PermissionSet;

namespace Odin.Services.Membership.Connections
{
    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public class CircleNetworkService(
        ILogger<CircleNetworkService> logger,
        ExchangeGrantService exchangeGrantService,
        TenantContext tenantContext,
        IAppRegistrationService appRegistrationService,
        TenantSystemStorage tenantSystemStorage,
        CircleMembershipService circleMembershipService,
        IMediator mediator,
        CircleDefinitionService circleDefinitionService,
        DriveManager driveManager)
        : INotificationHandler<DriveDefinitionAddedNotification>,
            INotificationHandler<AppRegistrationChangedNotification>
    {
        private readonly CircleNetworkStorage _storage = new(tenantSystemStorage, circleMembershipService);

        /// <summary>
        /// Creates a <see cref="PermissionContext"/> for the specified caller based on their access
        /// </summary>
        public async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreateTransitPermissionContext(
            OdinId odinId,
            ClientAuthenticationToken remoteIcrToken,
            IOdinContext odinContext)
        {
            var db = tenantSystemStorage.IdentityDatabase;

            logger.LogDebug("Creating transit permission context for [{odinId}]", odinId);

            var icr = await this.GetIdentityConnectionRegistration(odinId, remoteIcrToken);

            if (!icr.AccessGrant?.IsValid() ?? false)
            {
                logger.LogDebug("Creating transit permission context for [{odinId}] - Failed due to invalid access grant", odinId);
                throw new OdinSecurityException("Invalid token")
                {
                    IsRemoteIcrIssue = true
                };
            }

            if (!icr.IsConnected())
            {
                logger.LogDebug("Creating transit permission context for [{odinId}] - Failed due to invalid connection", odinId);
                throw new OdinSecurityException("Invalid connection")
                {
                    IsRemoteIcrIssue = true
                };
            }

            var (permissionContext, enabledCircles) = await CreatePermissionContextInternal(
                icr: icr,
                authToken: remoteIcrToken,
                accessReg: icr.AccessGrant!.AccessRegistration,
                applyAppCircleGrants: true,
                odinContext);

            return (permissionContext, enabledCircles);
        }

        /// <summary>
        /// Tries to create caller and permission context for the given OdinId if is connected
        /// </summary>
        public async Task<IOdinContext> TryCreateConnectedYouAuthContext(OdinId odinId, ClientAuthenticationToken authToken,
            AccessRegistration accessReg,
            IOdinContext odinContext)
        {
            var db = tenantSystemStorage.IdentityDatabase;

            logger.LogDebug("TryCreateConnectedYouAuthContext for {id}", odinId);

            var icr = await GetIdentityConnectionRegistrationInternal(odinId);
            bool isValid = icr.AccessGrant?.IsValid() ?? false;
            bool isConnected = icr.IsConnected();

            if (icr.Status == ConnectionStatus.Blocked)
            {
                return null;
            }

            // Only return the permissions if the identity is connected.
            if (isValid && isConnected)
            {
                var (permissionContext, enabledCircles) = await CreatePermissionContextInternal(
                    icr: icr,
                    accessReg: accessReg,
                    authToken: authToken,
                    applyAppCircleGrants: false,
                    odinContext: odinContext);

                var context = new OdinContext()
                {
                    Caller = new CallerContext(
                        odinId: odinId,
                        masterKey: null,
                        securityLevel: SecurityGroupType.Connected,
                        circleIds: enabledCircles)
                };

                context.SetPermissionContext(permissionContext);
                return context;
            }

            //TODO: what about blocked??

            return null;
        }

        /// <summary>
        /// Disconnects you from the specified <see cref="OdinId"/>
        /// </summary>
        public async Task<bool> Disconnect(OdinId odinId, IOdinContext odinContext)
        {
            var db = tenantSystemStorage.IdentityDatabase;

            odinContext.AssertCanManageConnections();

            var info = await this.GetIdentityConnectionRegistration(odinId, odinContext);
            if (info is { Status: ConnectionStatus.Connected })
            {
                _storage.Delete(odinId);

                await mediator.Publish(new IdentityConnectionRegistrationChangedNotification()
                {
                    OdinId = odinId,
                    OdinContext = odinContext,
                    db = db
                });

                return true;
            }

            return false;
        }

        /// <summary>
        /// Blocks the specified <see cref="OdinId"/> from your network
        /// </summary>
        public async Task<bool> Block(OdinId odinId, IOdinContext odinContext)
        {
            odinContext.AssertCanManageConnections();

            var info = await this.GetIdentityConnectionRegistration(odinId, odinContext);

            //TODO: when you block a connection, you must also destroy exchange grant

            if (null != info && info.Status == ConnectionStatus.Connected)
            {
                info.Status = ConnectionStatus.Blocked;
                this.SaveIcr(info, odinContext);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets profiles that have been marked as <see cref="ConnectionStatus.Blocked"/>
        /// </summary>
        public async Task<CursoredResult<long, IdentityConnectionRegistration>> GetBlockedProfiles(int count, long cursor,
            IOdinContext odinContext)
        {
            return await Task.FromResult(this.GetConnectionsInternal(count, cursor, ConnectionStatus.Blocked, odinContext));
        }

        /// <summary>
        /// Returns a list of identities which are connected to this DI
        /// </summary>
        public async Task<CursoredResult<long, IdentityConnectionRegistration>> GetConnectedIdentities(int count, long cursor,
            IOdinContext odinContext)
        {
            return await Task.FromResult(this.GetConnectionsInternal(count, cursor, ConnectionStatus.Connected, odinContext));
        }

        /// <summary>
        /// Unblocks the specified <see cref="OdinId"/> from your network
        /// </summary>
        public async Task<bool> Unblock(OdinId odinId, IOdinContext odinContext)
        {
            odinContext.AssertCanManageConnections();

            var info = await this.GetIdentityConnectionRegistration(odinId, odinContext);
            if (null != info && info.Status == ConnectionStatus.Blocked)
            {
                info.Status = ConnectionStatus.Connected;
                this.SaveIcr(info, odinContext);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the current connection info
        /// </summary>
        /// <param name="odinId"></param>
        /// <param name="odinContext"></param>
        /// <param name="overrideHack"></param>
        /// <returns></returns>
        public async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistration(OdinId odinId, IOdinContext odinContext,
            bool overrideHack = false)
        {
            //TODO: need to cache here?
            //HACK: DOING THIS WHILE DESIGNING x-token - REMOVE THIS
            if (!overrideHack)
            {
                odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
            }

            return await GetIdentityConnectionRegistrationInternal(odinId);
        }

        /// <summary>
        /// Gets the connection info if the specified <param name="remoteClientAuthenticationToken">x-token half key</param> is valid
        /// </summary>
        /// <param name="odinId"></param>
        /// <param name="remoteClientAuthenticationToken"></param>
        /// <returns></returns>
        public async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistration(
            OdinId odinId,
            ClientAuthenticationToken remoteClientAuthenticationToken)
        {
            var connection = await GetIdentityConnectionRegistrationInternal(odinId);

            if (connection?.AccessGrant?.AccessRegistration == null)
            {
                throw new OdinSecurityException("Unauthorized Action") { IsRemoteIcrIssue = true };
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
        public async Task<AccessRegistration> GetIdentityConnectionAccessRegistration(OdinId odinId,
            SensitiveByteArray remoteIdentityConnectionKey)
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
        public async Task<bool> IsConnected(OdinId odinId, IOdinContext odinContext)
        {
            //allow the caller to see if s/he is connected, otherwise
            if (odinContext.Caller.OdinId != odinId)
            {
                //TODO: this needs to be changed to - can view connections
                odinContext.AssertCanManageConnections();
            }

            var info = await this.GetIdentityConnectionRegistration(odinId, odinContext);
            return info.Status == ConnectionStatus.Connected;
        }

        public async Task<IEnumerable<OdinId>> GetCircleMembers(GuidId circleId, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
            //added override:true because PermissionKeys.ReadCircleMembership is present
            var result = circleMembershipService.GetDomainsInCircle(circleId, odinContext, overrideHack: true)
                .Where(d => d.DomainType == DomainType.Identity)
                .Select(m => new OdinId(m.Domain));
            return await Task.FromResult(result);
        }

        /// <summary>
        /// Throws an exception if the odinId is blocked.
        /// </summary>
        /// <param name="odinId"></param>
        /// <param name="odinContext"></param>
        /// <returns></returns>
        public async Task AssertConnectionIsNoneOrValid(OdinId odinId, IOdinContext odinContext)
        {
            var info = await this.GetIdentityConnectionRegistration(odinId, odinContext);
            this.AssertConnectionIsNoneOrValid(info);
        }

        /// <summary>
        /// Adds the specified odinId to your network
        /// </summary>
        /// <param name="odinIdentity">The public key certificate containing the domain name which will be connected</param>
        /// <param name="accessGrant">The access to be given to this connection</param>
        /// <param name="encryptedCat">The keys used when accessing the remote identity</param>
        /// <param name="contactData"></param>
        /// <param name="odinContext"></param>
        /// <returns></returns>
        public Task Connect(string odinIdentity, AccessExchangeGrant accessGrant, EncryptedClientAccessToken encryptedCat,
            ContactRequestData contactData,
            IOdinContext odinContext)
        {
            //TODO: need to add security that this method can be called

            if (encryptedCat == null || encryptedCat.EncryptedData.KeyEncrypted.Length == 0)
            {
                throw new OdinSecurityException("Invalid EncryptedClientAccessToken");
            }

            var odinId = (OdinId)odinIdentity;

            //Note: we will just overwrite the record
            //1. validate current connection state
            // var info = await this.GetIdentityConnectionRegistrationInternal(odinId);

            // if (info.Status != ConnectionStatus.None)
            // {
            //     throw new OdinSecurityException("invalid connection state");
            // }

            //TODO: need to scan the YouAuthServiceClassic to see if this user has a HomeAppIdentityRegistration

            //2. add the record to the list of connections
            var newConnection = new IdentityConnectionRegistration()
            {
                OdinId = odinId,
                Status = ConnectionStatus.Connected,
                Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                OriginalContactData = contactData,
                AccessGrant = accessGrant,
                EncryptedClientAccessToken = encryptedCat
            };

            this.SaveIcr(newConnection, odinContext);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gives access to all resource granted by the specified circle to the odinId
        /// </summary>
        public async Task GrantCircle(GuidId circleId, OdinId odinId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var icr = await this.GetIdentityConnectionRegistrationInternal(odinId);

            if (icr == null || !icr.IsConnected())
            {
                throw new OdinSecurityException($"{odinId} must have valid connection to be added to a circle");
            }

            if (icr.AccessGrant.CircleGrants.TryGetValue(circleId, out _))
            {
                //TODO: Here we should ensure it's in the _circleMemberStorage just in case this was called because it's out of sync
                throw new OdinClientException($"{odinId} is already member of circle", OdinClientErrorCode.IdentityAlreadyMemberOfCircle);
            }

            var circleDefinition = circleMembershipService.GetCircle(circleId, odinContext);
            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            var circleGrant = await circleMembershipService.CreateCircleGrant(circleDefinition, keyStoreKey, masterKey, odinContext);

            icr.AccessGrant.CircleGrants.Add(circleGrant.CircleId, circleGrant);

            //
            // Check the apps.  If the circle being granted is authorized by an app
            // ensure the new member gets the permissions given by the app
            //
            var allApps = await appRegistrationService.GetRegisteredApps(odinContext);
            var appsThatGrantThisCircle = allApps.Where(reg => reg?.AuthorizedCircles?.Any(c => c == circleId) ?? false);

            foreach (var app in appsThatGrantThisCircle)
            {
                var appCircleGrant = await this.CreateAppCircleGrant(app, circleId, keyStoreKey, masterKey);
                icr.AccessGrant.AddUpdateAppCircleGrant(appCircleGrant);
            }

            keyStoreKey.Wipe();
            this.SaveIcr(icr, odinContext);
        }

        /// <summary>
        /// Removes drives and permissions of the specified circle from the odinId
        /// </summary>
        public async Task RevokeCircleAccess(GuidId circleId, OdinId odinId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

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
            foreach (var (_, appCircleGrants) in icr.AccessGrant.AppGrants)
            {
                appCircleGrants.Remove(circleId.Value);
            }

            this.SaveIcr(icr, odinContext);
        }

        public async Task<Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>> CreateAppCircleGrantListWithSystemCircle(
            List<GuidId> circleIds,
            SensitiveByteArray keyStoreKey,
            IOdinContext odinContext)
        {
            // Always put identities in the system circle
            var list = circleIds ?? new List<GuidId>();
            list.Add(SystemCircleConstants.ConnectedIdentitiesSystemCircleId);
            return await this.CreateAppCircleGrantList(list, keyStoreKey, odinContext);
        }


        public async Task<Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>> CreateAppCircleGrantList(
            List<GuidId> circleIds,
            SensitiveByteArray keyStoreKey,
            IOdinContext odinContext)
        {
            var masterKey = odinContext.Caller.GetMasterKey();

            var allApps = await appRegistrationService.GetRegisteredApps(odinContext);
            var appGrants = new Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>();

            foreach (var circleId in circleIds)
            {
                var appsThatGrantThisCircle = allApps.Where(reg => reg?.AuthorizedCircles?.Any(c => c == circleId) ?? false);

                foreach (var app in appsThatGrantThisCircle)
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
        /// Updates a <see cref="CircleDefinition"/> and applies permission and drive changes to all existing circle members
        /// </summary>
        public async Task UpdateCircleDefinition(CircleDefinition circleDef, IOdinContext odinContext)
        {
            await circleMembershipService.AssertValidDriveGrants(circleDef.DriveGrants);

            var members = await GetCircleMembers(circleDef.Id, odinContext);
            var masterKey = odinContext.Caller.GetMasterKey();

            // List<OdinId> invalidMembers = new List<OdinId>();
            foreach (var odinId in members)
            {
                var icr = await this.GetIdentityConnectionRegistrationInternal(odinId);

                var circleKey = circleDef.Id;
                var hasCg = icr.AccessGrant.CircleGrants.Remove(circleKey, out _);

                if (icr.IsConnected() && hasCg)
                {
                    // Re-create the circle grant so 
                    var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
                    icr.AccessGrant.CircleGrants[circleKey] =
                        await circleMembershipService.CreateCircleGrant(circleDef, keyStoreKey, masterKey, odinContext);
                    keyStoreKey.Wipe();
                }
                else
                {
                    //It should not occur that a circle has a member
                    //who is not connected but let's capture it
                    // invalidMembers.Add(odinId);
                }

                this.SaveIcr(icr, odinContext);
            }

            await circleMembershipService.Update(circleDef, odinContext);

            //TODO: determine how to handle invalidMembers - do we return to the UI?  do we remove from all circles?
        }

        /// <summary>
        /// Tests if a circle has members and indicates if it can be deleted
        /// </summary>
        public async Task DeleteCircleDefinition(GuidId circleId, IOdinContext odinContext)
        {
            var members = await this.GetCircleMembers(circleId, odinContext);

            if (members.Any())
            {
                throw new OdinClientException("Cannot delete a circle with members", OdinClientErrorCode.CannotDeleteCircleWithMembers);
            }

            await circleMembershipService.Delete(circleId, odinContext);
        }

        public async Task Handle(DriveDefinitionAddedNotification notification, CancellationToken cancellationToken)
        {
            var odinContext = notification.OdinContext;
            if (notification.IsNewDrive)
            {
                await HandleDriveAdded(notification.Drive, odinContext);
            }
            else
            {
                await HandleDriveUpdated(notification.Drive, odinContext);
            }
        }

        public async Task Handle(AppRegistrationChangedNotification notification, CancellationToken cancellationToken)
        {
            var odinContext = notification.OdinContext;
            await this.ReconcileAuthorizedCircles(notification.OldAppRegistration?.Redacted(), notification.NewAppRegistration.Redacted(),
                odinContext);
        }

        public async Task RevokeConnection(OdinId odinId, IOdinContext odinContext)
        {
            var db = tenantSystemStorage.IdentityDatabase;

            _storage.Delete(odinId);
            await mediator.Publish(new IdentityConnectionRegistrationChangedNotification()
            {
                OdinId = odinId,
                OdinContext = odinContext,
                db = db
            });
        }

        public async Task<IcrTroubleshootingInfo> GetTroubleshootingInfo(OdinId odinId, IOdinContext odinContext)
        {
            var db = tenantSystemStorage.IdentityDatabase;

            odinContext.Caller.AssertHasMasterKey();

            // Need to see if the circle has the correct drives
            // 
            // SystemCircleConstants.ConnectedIdentitiesSystemCircleInitialDrives

            var info = new IcrTroubleshootingInfo();
            var circleDefinitions = (await circleDefinitionService.GetCircles(true)).ToList();
            var icr = await GetIdentityConnectionRegistrationInternal(odinId);

            ArgumentNullException.ThrowIfNull(icr);
            ArgumentNullException.ThrowIfNull(icr.AccessGrant);
            ArgumentNullException.ThrowIfNull(icr.AccessGrant.CircleGrants);

            // Get all circles on identity
            foreach (var definition in circleDefinitions)
            {
                ArgumentNullException.ThrowIfNull(definition);

                var isCircleMember = icr.AccessGrant.CircleGrants.TryGetValue(definition.Id, out var circleGrant);
                var hasCircleGrant = circleGrant != null;

                var summary = isCircleMember ? "Identity is in this circle" : "Identity is not a member of this circle";
                var actualPermissionKeys = circleGrant?.PermissionSet?.Redacted() ?? new RedactedPermissionSet() { Keys = [] };
                var permissionKeysMatch = definition.Permissions.Keys.Order().SequenceEqual(actualPermissionKeys.Keys.Order());

                var ci = new CircleInfo()
                {
                    CircleDefinitionId = definition.Id,
                    CircleDefinitionName = definition.Name,
                    CircleDefinitionDriveGrantCount = definition.DriveGrants?.Count() ?? 0,
                    Analysis = new CircleAnalysis()
                    {
                        IsCircleMember = isCircleMember,
                        Summary = summary,
                        PermissionKeysAreValid = permissionKeysMatch,
                        ExpectedPermissionKeys = definition.Permissions.Redacted(),
                        ActualPermissionKeys = actualPermissionKeys,
                        DriveGrantAnalysis = new List<DriveGrantInfo>(),
                    }
                };

                if (isCircleMember && definition.DriveGrants != null && hasCircleGrant)
                {
                    foreach (var expectedDriveGrant in definition.DriveGrants)
                    {
                        var driveId = await driveManager.GetDriveIdByAlias(expectedDriveGrant.PermissionedDrive.Drive, db);
                        var driveInfo = await driveManager.GetDrive(driveId.GetValueOrDefault(), db);

                        var grantedDrive = circleGrant.KeyStoreKeyEncryptedDriveGrants.SingleOrDefault(dg =>
                            dg.PermissionedDrive == expectedDriveGrant.PermissionedDrive);

                        //you must have drive-read permission to get the Key Store Key
                        // you can have write permission w/o having the storage key
                        var driveIsGranted = grantedDrive != null;
                        var encryptedKeyLength = grantedDrive?.KeyStoreKeyEncryptedStorageKey?.KeyEncrypted?.Length ?? 0;

                        var expectedDrivePermission = expectedDriveGrant.PermissionedDrive.Permission;
                        var actualDrivePermission = grantedDrive?.PermissionedDrive.Permission ?? DrivePermission.None;

                        var drivePermissionIsValid = expectedDrivePermission == actualDrivePermission;
                        var hasValidEncryptionKey = true;
                        if (expectedDrivePermission.HasFlag(DrivePermission.Read))
                        {
                            hasValidEncryptionKey = encryptedKeyLength > 0;
                        }

                        var isValid = driveIsGranted && drivePermissionIsValid && hasValidEncryptionKey;
                        var dgi = new DriveGrantInfo()
                        {
                            DriveName = driveInfo.Name,
                            TargetDrive = driveInfo.TargetDriveInfo,
                            DrivePermissionIsValid = drivePermissionIsValid,
                            HasValidEncryptionKey = hasValidEncryptionKey,
                            DriveGrantIsValid = isValid,
                            DriveIsGranted = driveIsGranted,
                            ExpectedDrivePermission = expectedDrivePermission,
                            ActualDrivePermission = actualDrivePermission,
                            EncryptedKeyLength = encryptedKeyLength
                        };

                        ci.Analysis.DriveGrantAnalysis.Add(dgi);
                    }
                }

                info.Circles.Add(ci);
            }

            return info;
        }

        public Task<PeerIcrClient> GetPeerIcrClient(Guid accessRegId)
        {
            return Task.FromResult(_storage.GetPeerIcrClient(accessRegId));
        }

        public async Task<ClientAccessToken> CreatePeerIcrClientForCaller(IOdinContext odinContext)
        {
            odinContext.Caller.AssertIsConnected();
            var caller = odinContext.GetCallerOdinIdOrFail();

            var grantKeyStoreKey = odinContext.PermissionsContext.GetKeyStoreKey();
            var (accessRegistration, token) = await exchangeGrantService.CreateClientAccessToken(
                grantKeyStoreKey, ClientTokenType.RemoteNotificationSubscriber);

            var client = new PeerIcrClient
            {
                Identity = caller,
                AccessRegistration = accessRegistration
            };

            _storage.SavePeerIcrClient(client);
            return token;
        }

        private async Task<AppCircleGrant> CreateAppCircleGrant(
            RedactedAppRegistration appReg,
            GuidId circleId,
            SensitiveByteArray keyStoreKey,
            SensitiveByteArray masterKey)
        {
            //map the exchange grant to a structure that matches ICR
            var grant = await exchangeGrantService.CreateExchangeGrant(
                tenantSystemStorage.IdentityDatabase,
                keyStoreKey,
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


        private async Task HandleDriveUpdated(StorageDrive drive, IOdinContext odinContext)
        {
            //examine system circle; remove drive if needed
            CircleDefinition systemCircle =
                circleMembershipService.GetCircle(SystemCircleConstants.ConnectedIdentitiesSystemCircleId, odinContext);

            var existingDriveGrant = systemCircle.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == drive.TargetDriveInfo);
            if (drive.AllowAnonymousReads == false && existingDriveGrant != null)
            {
                //remove the drive as it no longer allows anonymous reads
                systemCircle.DriveGrants =
                    systemCircle.DriveGrants.Where(dg => dg.PermissionedDrive.Drive != drive.TargetDriveInfo).ToList();
                await this.UpdateCircleDefinition(systemCircle, odinContext);
                return;
            }

            if (drive.AllowAnonymousReads && null == existingDriveGrant)
            {
                //act like it's new
                await this.HandleDriveAdded(drive, odinContext);
            }
        }

        /// <summary>
        /// Updates the system circle's drive grants
        /// </summary>
        private async Task HandleDriveAdded(StorageDrive drive, IOdinContext odinContext)
        {
            //only add anonymous drives
            if (drive.AllowAnonymousReads == false)
            {
                return;
            }

            CircleDefinition def = circleMembershipService.GetCircle(SystemCircleConstants.ConnectedIdentitiesSystemCircleId, odinContext);

            var grants = def.DriveGrants?.ToList() ?? new List<DriveGrantRequest>();
            grants.Add(new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = drive.TargetDriveInfo,
                    Permission = DrivePermission.Read
                }
            });

            def.DriveGrants = grants;
            await this.UpdateCircleDefinition(def, odinContext);
        }


        private async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreatePermissionContextInternal(
            IdentityConnectionRegistration icr,
            ClientAuthenticationToken authToken,
            AccessRegistration accessReg,
            bool applyAppCircleGrants,
            IOdinContext odinContext)
        {
            // Note: the icr.AccessGrant.AccessRegistration and parameter accessReg might not be the same in the case of YouAuth; this is intentional 

            var (grants, enabledCircles) =
                circleMembershipService.MapCircleGrantsToExchangeGrants(icr.OdinId.AsciiDomain,
                    icr.AccessGrant.CircleGrants.Values.ToList(), odinContext);

            if (applyAppCircleGrants)
            {
                logger.LogDebug("CreatePermissionContextInternal -> applying app circle grants");
                foreach (var (appId, appCircleGrantDictionary) in icr.AccessGrant.AppGrants)
                {
                    foreach (var (_, appCg) in appCircleGrantDictionary)
                    {
                        var alreadyEnabledCircle = enabledCircles.Exists(cid => cid == appCg.CircleId);
                        if (alreadyEnabledCircle || circleDefinitionService.IsEnabled(appCg.CircleId))
                        {
                            if (!alreadyEnabledCircle)
                            {
                                enabledCircles.Add(appCg.CircleId);
                            }

                            if (grants.ContainsKey(appId))
                            {
                                //TODO: figuring out a production issue; it seems it is granted twice
                                if (grants.TryGetValue(appId, out var v))
                                {
                                    var existingKeyJson = OdinSystemSerializer.Serialize(v.Redacted());
                                    var newKeyJson = OdinSystemSerializer.Serialize(appCg.Redacted());

                                    if (existingKeyJson != newKeyJson)
                                    {
                                        var message =
                                            $"Grantee [{icr.OdinId.AsciiDomain}] has key with appId value [{appId}] which already exists in grants.  The values are equivalent";
                                        logger.LogInformation(message);
                                    }
                                    else
                                    {
                                        var message =
                                            $"Grantee [{icr.OdinId.AsciiDomain}] has key with appId value [{appId}] which already exists in grants.  The values do not match";
                                        message += $"\n Existing key has [{existingKeyJson}]";
                                        message += $"\n AppGrant Key [{newKeyJson}]";
                                        logger.LogError(message);
                                    }
                                }
                                else
                                {
                                    logger.LogWarning(
                                        $"Wild; so wild. grants.ContainsKey says it has {appId} but grants.TryGetValues does not???");
                                }
                            }
                            else
                            {
                                grants.Add(appId, new ExchangeGrant()
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
            }

            //TODO: only add this if I follow this identity and this is for transit
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var feedDriveWriteGrant = await exchangeGrantService.CreateExchangeGrant(tenantSystemStorage.IdentityDatabase, keyStoreKey,
                new Permissions_PermissionSet(),
                new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new()
                        {
                            Drive = SystemDriveConstants.FeedDrive,
                            Permission = DrivePermission.Write
                        }
                    }
                },
                masterKey: null,
                icrKey: null);

            grants.Add(ByteArrayUtil.ReduceSHA256Hash("feed_drive_writer"), feedDriveWriteGrant);

            var permissionKeys = tenantContext.Settings.GetAdditionalPermissionKeysForConnectedIdentities();
            var anonDrivePermissions = tenantContext.Settings.GetAnonymousDrivePermissionsForConnectedIdentities();

            var permissionCtx = await exchangeGrantService.CreatePermissionContext(
                authToken: authToken,
                grants: grants,
                accessReg: accessReg,
                odinContext: odinContext,
                db: tenantSystemStorage.IdentityDatabase,
                additionalPermissionKeys: permissionKeys,
                includeAnonymousDrives: true,
                anonymousDrivePermission: anonDrivePermissions);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Start Final Permission Context:");

                try
                {
                    var redacted = permissionCtx.Redacted();
                    logger.LogDebug("Enabled Circles: [{k}]", string.Join(",", enabledCircles));

                    foreach (var pg in redacted.PermissionGroups)
                    {
                        logger.LogDebug("Start Permission Group");
                        logger.LogDebug("PermissionKeys: [{k}]", string.Join(",", pg.PermissionSet.Keys ?? []));
                        logger.LogDebug("Drive Grants: [{dg}]", string.Join("|", pg.DriveGrants ?? []));
                        logger.LogDebug("End Permission Group");
                    }

                    logger.LogDebug("End Final Permission Context:");
                }
                catch (Exception e)
                {
                    logger.LogDebug(e, "Failure while logging final permission context");
                }
            }

            var result = (permissionCtx, enabledCircles);
            return await Task.FromResult(result);
        }


        private CursoredResult<long, IdentityConnectionRegistration> GetConnectionsInternal(int count, long cursor, ConnectionStatus status,
            IOdinContext odinContext)
        {
            var db = tenantSystemStorage.IdentityDatabase;

            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);

            var list = _storage.GetList(count, new UnixTimeUtcUnique(cursor), out var nextCursor, status);
            return new CursoredResult<long, IdentityConnectionRegistration>()
            {
                Cursor = nextCursor.GetValueOrDefault().uniqueTime,
                Results = list
            };
        }

        /// <summary>
        /// Throws an exception if the odinId is blocked.
        /// </summary>
        /// <param name="registration">The connection info to be checked</param>
        /// <returns></returns>
        private void AssertConnectionIsNoneOrValid(IdentityConnectionRegistration registration)
        {
            if (registration.Status == ConnectionStatus.Blocked)
            {
                throw new SecurityException("OdinId is blocked");
            }
        }

        private async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistrationInternal(OdinId odinId)
        {
            var registration = _storage.Get(odinId);

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

        private void SaveIcr(IdentityConnectionRegistration icr, IOdinContext odinContext)
        {
            var db = tenantSystemStorage.IdentityDatabase;

            //TODO: this is a critical change; need to audit this
            if (icr.Status == ConnectionStatus.None)
            {
                _storage.Delete(icr.OdinId);
            }
            else
            {
                _storage.Upsert(icr, odinContext);
            }

            //notify anyone caching data for this identity, we need to reset the cache
            mediator.Publish(new IdentityConnectionRegistrationChangedNotification()
            {
                OdinId = icr.OdinId,
                OdinContext = odinContext,
                db = db
            });
        }

        public async Task ReconcileAuthorizedCircles(RedactedAppRegistration oldAppRegistration, RedactedAppRegistration newAppRegistration,
            IOdinContext odinContext)
        {
            var masterKey = odinContext.Caller.GetMasterKey();
            var appKey = newAppRegistration.AppId.Value;

            //TODO: use _db.CreateCommitUnitOfWork()
            if (null != oldAppRegistration)
            {
                var circlesToRevoke = oldAppRegistration.AuthorizedCircles.Except(newAppRegistration.AuthorizedCircles);
                //TODO: spin thru circles to revoke an update members

                foreach (var circleId in circlesToRevoke)
                {
                    //get all circle members and update their grants
                    var members = await this.GetCircleMembers(circleId, odinContext);

                    foreach (var odinId in members)
                    {
                        var icr = await this.GetIdentityConnectionRegistrationInternal(odinId);
                        var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
                        icr.AccessGrant.AppGrants[appKey]?.Remove(circleId);
                        keyStoreKey.Wipe();
                        this.SaveIcr(icr, odinContext);
                    }
                }
            }

            foreach (var circleId in newAppRegistration.AuthorizedCircles ?? new List<Guid>())
            {
                //get all circle members and update their grants
                var members = await this.GetCircleMembers(circleId, odinContext);

                foreach (var odinId in members)
                {
                    var icr = await this.GetIdentityConnectionRegistrationInternal(odinId);
                    var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);

                    var appCircleGrant = await this.CreateAppCircleGrant(newAppRegistration, circleId, keyStoreKey, masterKey);

                    if (!icr.AccessGrant.AppGrants.TryGetValue(appKey, out var appCircleGrantDictionary))
                    {
                        appCircleGrantDictionary = new Dictionary<Guid, AppCircleGrant>();
                    }

                    appCircleGrantDictionary[appCircleGrant.CircleId] = appCircleGrant;
                    icr.AccessGrant.AppGrants[appKey] = appCircleGrantDictionary;

                    keyStoreKey.Wipe();

                    this.SaveIcr(icr, odinContext);
                }
            }
            //
        }
    }
}