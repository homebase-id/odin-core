using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Mediator;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Util;
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
        DriveManager driveManager,
        PublicPrivateKeyService publicPrivateKeyService)
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
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var icr = await this.GetIcr(odinId, remoteIcrToken, cn);

            if (!icr.AccessGrant?.IsValid() ?? false)
            {
                throw new OdinSecurityException("Invalid token")
                {
                    IsRemoteIcrIssue = true
                };
            }

            if (!icr.IsConnected())
            {
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
                odinContext,
                cn);

            return (permissionContext, enabledCircles);
        }

        /// <summary>
        /// Tries to create caller and permission context for the given OdinId if is connected
        /// </summary>
        public async Task<IOdinContext> TryCreateConnectedYouAuthContext(OdinId odinId, ClientAuthenticationToken authToken, AccessRegistration accessReg,
            IOdinContext odinContext, DatabaseConnection cn)
        {
            var icr = await GetIdentityConnectionRegistrationInternal(odinId, cn);
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
                    odinContext: odinContext,
                    cn: cn);


                var transientTempDrive = SystemDriveConstants.TransientTempDrive;
                var transientTempDriveGrant = new DriveGrant()
                {
                    DriveId = (await driveManager.GetDriveIdByAlias(transientTempDrive, cn)).GetValueOrDefault(),
                    PermissionedDrive = new()
                    {
                        Drive = transientTempDrive,
                        Permission = DrivePermission.Write
                    },
                    KeyStoreKeyEncryptedStorageKey = null
                };

                permissionContext.PermissionGroups.Add(
                    "grant_transient_temp_drive_to_connected_youauth_identity",
                    new PermissionGroup(
                        new PermissionSet(new[] { PermissionKeys.UseTransitWrite, PermissionKeys.ReadConnections }),
                        new List<DriveGrant>() { transientTempDriveGrant }, null, null));


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

            return null;
        }

        /// <summary>
        /// Disconnects you from the specified <see cref="OdinId"/>
        /// </summary>
        public async Task<bool> Disconnect(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.AssertCanManageConnections();

            var info = await this.GetIcr(odinId, odinContext, cn);
            if (info is { Status: ConnectionStatus.Connected })
            {
                _storage.Delete(odinId, cn);

                await mediator.Publish(new ConnectionDeletedNotification()
                {
                    OdinContext = odinContext,
                    OdinId = odinId,
                    DatabaseConnection = cn
                });

                return true;
            }

            return false;
        }

        /// <summary>
        /// Blocks the specified <see cref="OdinId"/> from your network
        /// </summary>
        public async Task<bool> Block(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.AssertCanManageConnections();

            var info = await this.GetIcr(odinId, odinContext, cn);

            //TODO: when you block a connection, you must also destroy exchange grant

            if (info.Status == ConnectionStatus.Connected)
            {
                info.Status = ConnectionStatus.Blocked;
                info.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                this.SaveIcr(info, odinContext, cn);

                await mediator.Publish(new ConnectionBlockedNotification
                {
                    OdinContext = odinContext,
                    OdinId = odinId,
                    DatabaseConnection = cn
                });

                return true;
            }

            if (info.Status == ConnectionStatus.Blocked || info.Status == ConnectionStatus.None)
            {
                info.Status = ConnectionStatus.Blocked;
                info.Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                info.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                this.SaveIcr(info, odinContext, cn);

                await mediator.Publish(new ConnectionBlockedNotification
                {
                    OdinContext = odinContext,
                    OdinId = odinId,
                    DatabaseConnection = cn
                });


                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets profiles that have been marked as <see cref="ConnectionStatus.Blocked"/>
        /// </summary>
        public async Task<CursoredResult<long, IdentityConnectionRegistration>> GetBlockedProfiles(int count, long cursor, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
            return await Task.FromResult(this.GetConnectionsInternal(count, cursor, ConnectionStatus.Blocked, odinContext, cn));
        }

        /// <summary>
        /// Returns a list of identities which are connected to this DI
        /// </summary>
        public async Task<CursoredResult<long, IdentityConnectionRegistration>> GetConnectedIdentities(int count, long cursor, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
            return await Task.FromResult(this.GetConnectionsInternal(count, cursor, ConnectionStatus.Connected, odinContext, cn));
        }

        /// <summary>
        /// Unblocks the specified <see cref="OdinId"/> from your network
        /// </summary>
        public async Task<bool> Unblock(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.AssertCanManageConnections();

            var info = await this.GetIcr(odinId, odinContext, cn);
            if (info.Status == ConnectionStatus.Blocked)
            {
                bool isValid = info.AccessGrant?.IsValid() ?? false;

                if (isValid)
                {
                    info.Status = ConnectionStatus.Connected;
                    this.SaveIcr(info, odinContext, cn);
                    return true;
                }

                info.Status = ConnectionStatus.None;
                this.SaveIcr(info, odinContext, cn);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the current connection info
        /// </summary>
        /// <returns></returns>
        public async Task<IdentityConnectionRegistration> GetIcr(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn,
            bool overrideHack = false)
        {
            //TODO: need to cache here?
            //HACK: DOING THIS WHILE DESIGNING x-token - REMOVE THIS
            if (!overrideHack)
            {
                odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
            }

            return await GetIdentityConnectionRegistrationInternal(odinId, cn);
        }

        /// <summary>
        /// Gets the connection info if the specified <param name="remoteClientAuthenticationToken">x-token half key</param> is valid
        /// </summary>
        public async Task<IdentityConnectionRegistration> GetIcr(
            OdinId odinId,
            ClientAuthenticationToken remoteClientAuthenticationToken,
            DatabaseConnection cn)
        {
            var connection = await GetIdentityConnectionRegistrationInternal(odinId, cn);

            if (connection?.AccessGrant?.AccessRegistration == null)
            {
                throw new OdinSecurityException("Unauthorized Action") { IsRemoteIcrIssue = true };
            }

            connection.AccessGrant.AccessRegistration.AssertValidRemoteKey(remoteClientAuthenticationToken.AccessTokenHalfKey);

            return connection;
        }


        /// <summary>
        /// Determines if the specified odinId is connected 
        /// </summary>
        public async Task<bool> IsConnected(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn)
        {
            //allow the caller to see if s/he is connected, otherwise
            if (odinContext.Caller.OdinId != odinId)
            {
                odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
            }

            var info = await this.GetIcr(odinId, odinContext, cn);
            return info.Status == ConnectionStatus.Connected;
        }

        public async Task<IEnumerable<OdinId>> GetCircleMembers(GuidId circleId, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
            //added override:true because PermissionKeys.ReadCircleMembership is present
            var result = circleMembershipService.GetDomainsInCircle(circleId, odinContext, cn, overrideHack: true)
                .Where(d => d.DomainType == DomainType.Identity)
                .Select(m => new OdinId(m.Domain));
            return await Task.FromResult(result);
        }

        /// <summary>
        /// Throws an exception if the odinId is blocked.
        /// </summary>
        public async Task AssertConnectionIsNoneOrValid(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn)
        {
            var info = await this.GetIcr(odinId, odinContext, cn);
            this.AssertConnectionIsNoneOrValid(info);
        }

        /// <summary>
        /// Adds the specified odinId to your network
        /// </summary>
        /// <returns></returns>
        public async Task Connect(string odinIdentity, AccessExchangeGrant accessGrant,
            (EncryptedClientAccessToken EncryptedCat, (EccEncryptedPayload Token, EccEncryptedPayload KeyStoreKey) Temp) keys,
            ContactRequestData contactData,
            ConnectionRequestOrigin connectionRequestOrigin,
            OdinId? introducerOdinId,
            byte[] verificationHash,
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            //TODO: need to add security that this method can be called

            var odinId = (OdinId)odinIdentity;

            //TODO: need to scan the YouAuthServiceClassic to see if this user has a HomeAppIdentityRegistration

            // Add the record to the list of connections
            var newConnection = new IdentityConnectionRegistration()
            {
                OdinId = odinId,
                Status = ConnectionStatus.Connected,
                Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                OriginalContactData = contactData,
                AccessGrant = accessGrant,
                EncryptedClientAccessToken = keys.EncryptedCat,
                TemporaryWeakClientAccessToken = keys.Temp.Token,
                TempWeakKeyStoreKey = keys.Temp.KeyStoreKey,
                ConnectionRequestOrigin = connectionRequestOrigin,
                IntroducerOdinId = introducerOdinId,
                VerificationHash = verificationHash
            };

            this.SaveIcr(newConnection, odinContext, cn);

            await mediator.Publish(new ConnectionFinalizedNotification()
            {
                OdinId = odinId,
                OdinContext = odinContext,
                DatabaseConnection = cn
            });
        }

        /// <summary>
        /// Gives access to all resource granted by the specified circle to the odinId
        /// </summary>
        public async Task GrantCircle(GuidId circleId, OdinId odinId, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();

            var icr = await this.GetIdentityConnectionRegistrationInternal(odinId, cn);

            if (icr == null || !icr.IsConnected())
            {
                throw new OdinSecurityException($"{odinId} must have valid connection to be added to a circle");
            }

            if (icr.AccessGrant.CircleGrants.TryGetValue(SystemCircleConstants.AutoConnectionsCircleId, out _))
            {
                throw new OdinClientException($"Cannot grant additional circles to auto-connected identity.  You must first confirm the connection.",
                    OdinClientErrorCode.CannotGrantAutoConnectedMoreCircles);
            }

            if (icr.AccessGrant.CircleGrants.TryGetValue(circleId, out _))
            {
                //TODO: Here we should ensure it's in the _circleMemberStorage just in case this was called because it's out of sync
                throw new OdinClientException($"{odinId} is already member of circle", OdinClientErrorCode.IdentityAlreadyMemberOfCircle);
            }

            var circleDefinition = circleMembershipService.GetCircle(circleId, odinContext, cn);
            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            var circleGrant = await circleMembershipService.CreateCircleGrant(keyStoreKey, circleDefinition, masterKey, odinContext, cn);

            icr.AccessGrant.CircleGrants.Add(circleGrant.CircleId, circleGrant);

            //
            // Check the apps.  If the circle being granted is authorized by an app
            // ensure the new member gets the permissions given by the app
            //
            var allApps = await appRegistrationService.GetRegisteredApps(odinContext, cn);
            var appsThatGrantThisCircle = allApps.Where(reg => reg?.AuthorizedCircles?.Any(c => c == circleId) ?? false);

            foreach (var app in appsThatGrantThisCircle)
            {
                var appCircleGrant = await this.CreateAppCircleGrant(app, keyStoreKey, circleId, masterKey, cn);
                icr.AccessGrant.AddUpdateAppCircleGrant(appCircleGrant);
            }

            keyStoreKey.Wipe();
            this.SaveIcr(icr, odinContext, cn);
        }

        /// <summary>
        /// Removes drives and permissions of the specified circle from the odinId
        /// </summary>
        public async Task RevokeCircleAccess(GuidId circleId, OdinId odinId, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();

            var icr = await this.GetIdentityConnectionRegistrationInternal(odinId, cn);
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

            this.SaveIcr(icr, odinContext, cn);
        }

        public async Task<Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>> CreateAppCircleGrantListWithSystemCircle(
            SensitiveByteArray keyStoreKey,
            List<GuidId> circleIds,
            ConnectionRequestOrigin origin,
            SensitiveByteArray masterKey,
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var list = CircleNetworkUtils.EnsureSystemCircles(circleIds, origin);
            return await this.CreateAppCircleGrantList(keyStoreKey, list, masterKey, odinContext, cn);
        }


        public async Task<Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>> CreateAppCircleGrantList(
            SensitiveByteArray keyStoreKey,
            List<GuidId> circleIds,
            SensitiveByteArray masterKey,
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var allApps = await appRegistrationService.GetRegisteredApps(odinContext, cn);
            var appGrants = new Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>();

            foreach (var circleId in circleIds)
            {
                var appsThatGrantThisCircle = allApps.Where(reg => reg?.AuthorizedCircles?.Any(c => c == circleId) ?? false);

                foreach (var app in appsThatGrantThisCircle)
                {
                    var appKey = app.AppId.Value;
                    var appCircleGrant = await this.CreateAppCircleGrant(app, keyStoreKey, circleId, masterKey, cn);

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
        public async Task UpdateCircleDefinition(CircleDefinition circleDef, IOdinContext odinContext, DatabaseConnection cn)
        {
            await circleMembershipService.AssertValidDriveGrants(circleDef.DriveGrants, cn);

            var members = await GetCircleMembers(circleDef.Id, odinContext, cn);
            var masterKey = odinContext.Caller.GetMasterKey();

            // List<OdinId> invalidMembers = new List<OdinId>();
            foreach (var odinId in members)
            {
                var icr = await this.GetIdentityConnectionRegistrationInternal(odinId, cn);

                var circleKey = circleDef.Id;
                var hasCg = icr.AccessGrant.CircleGrants.Remove(circleKey, out _);

                if (icr.IsConnected() && hasCg)
                {
                    // Re-create the circle grant so 
                    var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
                    icr.AccessGrant.CircleGrants[circleKey] =
                        await circleMembershipService.CreateCircleGrant(keyStoreKey, circleDef, masterKey, odinContext, cn);
                    keyStoreKey.Wipe();
                }
                else
                {
                    //It should not occur that a circle has a member
                    //who is not connected but let's capture it
                    // invalidMembers.Add(odinId);
                }

                this.SaveIcr(icr, odinContext, cn);
            }

            await circleMembershipService.Update(circleDef, odinContext, cn);

            //TODO: determine how to handle invalidMembers - do we return to the UI?  do we remove from all circles?
        }

        /// <summary>
        /// Tests if a circle has members and indicates if it can be deleted
        /// </summary>
        public async Task DeleteCircleDefinition(GuidId circleId, IOdinContext odinContext, DatabaseConnection cn)
        {
            var members = await this.GetCircleMembers(circleId, odinContext, cn);

            if (members.Any())
            {
                throw new OdinClientException("Cannot delete a circle with members", OdinClientErrorCode.CannotDeleteCircleWithMembers);
            }

            await circleMembershipService.Delete(circleId, odinContext, cn);
        }

        public async Task Handle(DriveDefinitionAddedNotification notification, CancellationToken cancellationToken)
        {
            var odinContext = notification.OdinContext;
            if (notification.IsNewDrive)
            {
                await HandleDriveAdded(notification.Drive, odinContext, notification.DatabaseConnection);
            }
            else
            {
                await HandleDriveUpdated(notification.Drive, odinContext, notification.DatabaseConnection);
            }
        }

        public async Task Handle(AppRegistrationChangedNotification notification, CancellationToken cancellationToken)
        {
            var odinContext = notification.OdinContext;
            await this.ReconcileAuthorizedCircles(notification.OldAppRegistration?.Redacted(), notification.NewAppRegistration.Redacted(), odinContext,
                notification.DatabaseConnection);
        }

        public async Task RevokeConnection(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn)
        {
            _storage.Delete(odinId, cn);
            await mediator.Publish(new ConnectionDeletedNotification()
            {
                OdinContext = odinContext,
                OdinId = odinId,
                DatabaseConnection = cn
            });
        }

        public async Task<IcrTroubleshootingInfo> GetTroubleshootingInfo(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();

            // Need to see if the circle has the correct drives
            // 
            // SystemCircleConstants.ConnectedIdentitiesSystemCircleInitialDrives

            var info = new IcrTroubleshootingInfo();
            var circleDefinitions = (await circleDefinitionService.GetCircles(true, cn)).ToList();
            var icr = await GetIdentityConnectionRegistrationInternal(odinId, cn);

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
                        var driveId = await driveManager.GetDriveIdByAlias(expectedDriveGrant.PermissionedDrive.Drive, cn);
                        var driveInfo = await driveManager.GetDrive(driveId.GetValueOrDefault(), cn);

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

        public async Task ReconcileAuthorizedCircles(RedactedAppRegistration oldAppRegistration, RedactedAppRegistration newAppRegistration,
            IOdinContext odinContext,
            DatabaseConnection cn)
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
                    var members = await this.GetCircleMembers(circleId, odinContext, cn);

                    foreach (var odinId in members)
                    {
                        var icr = await this.GetIdentityConnectionRegistrationInternal(odinId, cn);
                        var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
                        icr.AccessGrant.AppGrants[appKey]?.Remove(circleId);
                        keyStoreKey.Wipe();
                        this.SaveIcr(icr, odinContext, cn);
                    }
                }
            }

            foreach (var circleId in newAppRegistration.AuthorizedCircles ?? new List<Guid>())
            {
                //get all circle members and update their grants
                var members = await this.GetCircleMembers(circleId, odinContext, cn);

                foreach (var odinId in members)
                {
                    var icr = await this.GetIdentityConnectionRegistrationInternal(odinId, cn);
                    var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);

                    var appCircleGrant = await this.CreateAppCircleGrant(newAppRegistration, keyStoreKey, circleId, masterKey, cn);

                    if (!icr.AccessGrant.AppGrants.TryGetValue(appKey, out var appCircleGrantDictionary))
                    {
                        appCircleGrantDictionary = new Dictionary<Guid, AppCircleGrant>();
                    }

                    appCircleGrantDictionary[appCircleGrant.CircleId] = appCircleGrant;
                    icr.AccessGrant.AppGrants[appKey] = appCircleGrantDictionary;

                    keyStoreKey.Wipe();

                    this.SaveIcr(icr, odinContext, cn);
                }
            }
            //
        }

        public async Task<VerifyConnectionResponse> VerifyConnectionCode(IOdinContext odinContext, DatabaseConnection cn)
        {
            if (!odinContext.Caller.IsConnected)
            {
                return new VerifyConnectionResponse
                {
                    IsConnected = false,
                    Hash = null
                };
            }

            //look up the verification hash on the caller's icr
            var callerIcr = await this.GetIcr(odinContext.GetCallerOdinIdOrFail(), odinContext, cn, true);

            if (callerIcr.VerificationHash?.Length == 0)
            {
                throw new OdinSecurityException("Cannot verify caller");
            }

            var result = new VerifyConnectionResponse()
            {
                IsConnected = odinContext.Caller.IsConnected,
                Hash = callerIcr.VerificationHash
            };

            return result;
        }

        /// <summary>
        /// Upgrades a connection which was created automatically (i.e. because of an introduction) to a confirmed connection
        /// </summary>
        public async Task ConfirmConnection(OdinId odinId, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();

            var icr = await this.GetIcr(odinId, odinContext, cn);

            if (!icr.IsConnected())
            {
                throw new OdinClientException("Cannot confirm identity that is not connected", OdinClientErrorCode.IdentityMustBeConnected);
            }

            if (!icr.AccessGrant.CircleGrants.TryGetValue(SystemCircleConstants.AutoConnectionsCircleId, out _))
            {
                throw new OdinClientException("Cannot confirm identity that is not in the AutoConnectionsCircle", OdinClientErrorCode.NotAnAutoConnection);
            }

            await cn.CreateCommitUnitOfWorkAsync(async () =>
            {
                await UpgradeTokenEncryptionIfNeeded(icr, odinContext, cn);
                await UpgradeKeyStoreKeyEncryptionIfNeeded(icr, odinContext, cn);

                await this.RevokeCircleAccess(SystemCircleConstants.AutoConnectionsCircleId, odinId, odinContext, cn);
                await this.GrantCircle(SystemCircleConstants.ConfirmedConnectionsCircleId, odinId, odinContext, cn);
            });
        }

        public async Task<bool> UpdateVerificationHash(OdinId odinId, Guid randomCode, IOdinContext odinContext, DatabaseConnection cn)
        {
            if (!odinContext.Caller.IsOwner)
            {
                odinContext.Caller.AssertCallerIsConnected();
                OdinValidationUtils.AssertIsTrue(odinId == odinContext.GetCallerOdinIdOrFail(), "caller does not match target identity");
            }

            var icr = await this.GetIcr(odinId, odinContext, cn);

            if (icr.Status == ConnectionStatus.Connected && icr.VerificationHash?.Length == 0)
            {
                // this should not occur since this process is running at the same time
                // we introduce the ability to have a null EncryptedClientAccessToken
                // for a connected identity; but #paranoid
                if (icr.EncryptedClientAccessToken == null)
                {
                    logger.LogWarning("Skipping UpdateVerificationHash since connected identity was missing EncryptedClientAccessToken");
                    return false;
                }

                var cat = icr.EncryptedClientAccessToken.Decrypt(odinContext.PermissionsContext.GetIcrKey());
                var hash = this.CreateVerificationHash(randomCode, cat.SharedSecret);

                icr.VerificationHash = hash;
                this.SaveIcr(icr, odinContext, cn);
                return true;
            }

            return false;
        }

        public byte[] CreateVerificationHash(Guid randomCode, SensitiveByteArray sharedSecret)
        {
            var combined = ByteArrayUtil.Combine(randomCode.ToByteArray(), sharedSecret.GetKey());
            var expectedHash = ByteArrayUtil.CalculateSHA256Hash(combined);
            return expectedHash;
        }

        public async Task UpgradeWeakClientAccessTokens(IOdinContext odinContext, DatabaseConnection cn)
        {
            var allIdentities = await this.GetConnectedIdentities(int.MaxValue, 0, odinContext, cn);
            foreach (var identity in allIdentities.Results)
            {
                try
                {
                    await UpgradeTokenEncryptionIfNeeded(identity, odinContext, cn);
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Failed while upgrading token for {identity}", identity);
                }

                if (odinContext.Caller.HasMasterKey)
                {
                    try
                    {
                        await UpgradeKeyStoreKeyEncryptionIfNeeded(identity, odinContext, cn);
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "Failed while upgrading KSK   for {identity}", identity);
                    }
                }
            }
        }

        private async Task<AppCircleGrant> CreateAppCircleGrant(
            RedactedAppRegistration appReg,
            SensitiveByteArray keyStoreKey,
            GuidId circleId,
            SensitiveByteArray masterKey,
            DatabaseConnection cn)
        {
            //map the exchange grant to a structure that matches ICR
            var grant = await exchangeGrantService.CreateExchangeGrant(
                cn,
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


        private async Task HandleDriveUpdated(StorageDrive drive, IOdinContext odinContext, DatabaseConnection cn)
        {
            async Task UpdateIfRequired(CircleDefinition def)
            {
                var existingDriveGrant = def.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == drive.TargetDriveInfo);
                if (drive.AllowAnonymousReads == false && existingDriveGrant != null)
                {
                    //remove the drive as it no longer allows anonymous reads
                    def.DriveGrants = def.DriveGrants.Where(dg => dg.PermissionedDrive.Drive != drive.TargetDriveInfo).ToList();
                    await this.UpdateCircleDefinition(def, odinContext, cn);
                    return;
                }

                if (drive.AllowAnonymousReads && null == existingDriveGrant)
                {
                    //act like it's new
                    await this.HandleDriveAdded(drive, odinContext, cn);
                }
            }

            CircleDefinition confirmedCircle = circleMembershipService.GetCircle(SystemCircleConstants.ConfirmedConnectionsCircleId, odinContext, cn);
            await UpdateIfRequired(confirmedCircle);

            CircleDefinition autoConnectedCircle = circleMembershipService.GetCircle(SystemCircleConstants.AutoConnectionsCircleId, odinContext, cn);
            await UpdateIfRequired(autoConnectedCircle);
        }

        /// <summary>
        /// Updates the system circles drive grants
        /// </summary>
        private async Task HandleDriveAdded(StorageDrive drive, IOdinContext odinContext, DatabaseConnection cn)
        {
            //only add anonymous drives
            if (drive.AllowAnonymousReads == false)
            {
                return;
            }

            async Task GrantAnonymousRead(CircleDefinition def)
            {
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
                await this.UpdateCircleDefinition(def, odinContext, cn);
            }

            CircleDefinition confirmedCircle = circleMembershipService.GetCircle(SystemCircleConstants.ConfirmedConnectionsCircleId, odinContext, cn);
            await GrantAnonymousRead(confirmedCircle);

            CircleDefinition autoConnectedCircle = circleMembershipService.GetCircle(SystemCircleConstants.AutoConnectionsCircleId, odinContext, cn);
            await GrantAnonymousRead(autoConnectedCircle);
        }


        private async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreatePermissionContextInternal(
            IdentityConnectionRegistration icr,
            ClientAuthenticationToken authToken,
            AccessRegistration accessReg,
            bool applyAppCircleGrants,
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            // Note: the icr.AccessGrant.AccessRegistration and parameter accessReg might not be the same in the case of YouAuth; this is intentional 

            var (grants, enabledCircles) =
                circleMembershipService.MapCircleGrantsToExchangeGrants(icr.AccessGrant.CircleGrants.Values.ToList(), odinContext, cn);

            if (applyAppCircleGrants)
            {
                foreach (var kvp in icr.AccessGrant.AppGrants)
                {
                    // var appId = kvp.Key;
                    var appCircleGrantDictionary = kvp.Value;

                    foreach (var (_, appCg) in appCircleGrantDictionary)
                    {
                        var alreadyEnabledCircle = enabledCircles.Exists(cid => cid == appCg.CircleId);
                        if (alreadyEnabledCircle || circleDefinitionService.IsEnabled(appCg.CircleId, cn))
                        {
                            if (!alreadyEnabledCircle)
                            {
                                enabledCircles.Add(appCg.CircleId);
                            }

                            if (grants.ContainsKey(kvp.Key))
                            {
                                //TODO: figuring out a production issue
                                if (grants.TryGetValue(kvp.Key, out var v))
                                {
                                    var existingKeyJson = OdinSystemSerializer.Serialize(v.Redacted());
                                    var newKeyJson = OdinSystemSerializer.Serialize(appCg);

                                    var message = $"Key with value [{kvp.Key}] already exists in grants.";
                                    message += $"\n Existing key has [{existingKeyJson}]";
                                    message += $"\n AppGrant Key [{newKeyJson}]";

                                    logger.LogWarning(message);
                                }
                                else
                                {
                                    logger.LogWarning($"Wild; so wild. grants.ContainsKey says it has {kvp.Key} but grants.TryGetValues does not???");
                                }
                            }
                            else
                            {
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
            }

            //TODO: only add this if I follow this identity and this is for transit
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var feedDriveWriteGrant = await exchangeGrantService.CreateExchangeGrant(cn, keyStoreKey, new Permissions_PermissionSet(),
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
                cn: cn,
                additionalPermissionKeys: permissionKeys,
                includeAnonymousDrives: true,
                anonymousDrivePermission: anonDrivePermissions);

            var result = (permissionCtx, enabledCircles);
            return await Task.FromResult(result);
        }


        private CursoredResult<long, IdentityConnectionRegistration> GetConnectionsInternal(int count, long cursor, ConnectionStatus status,
            IOdinContext odinContext, DatabaseConnection cn)
        {
            var list = _storage.GetList(count, new UnixTimeUtcUnique(cursor), out var nextCursor, status, cn);
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
                throw new OdinSecurityException("OdinId is blocked");
            }
        }

        private async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistrationInternal(OdinId odinId, DatabaseConnection cn)
        {
            var registration = _storage.Get(odinId, cn);

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

        private void SaveIcr(IdentityConnectionRegistration icr, IOdinContext odinContext, DatabaseConnection cn)
        {
            //TODO: this is a critical change; need to audit this
            if (icr.Status == ConnectionStatus.None)
            {
                _storage.Delete(icr.OdinId, cn);
            }
            else
            {
                _storage.Upsert(icr, odinContext, cn);
            }
        }

        public async Task UpgradeTokenEncryptionIfNeeded(IdentityConnectionRegistration identity, IOdinContext odinContext, DatabaseConnection cn)
        {
            if (identity.TemporaryWeakClientAccessToken != null)
            {
                logger.LogDebug("Upgrading ICR Token Encryption for {id}", identity.OdinId);

                var keyStoreKey = await publicPrivateKeyService.EccDecryptPayload(
                    PublicPrivateKeyType.OnlineIcrEncryptedKey,
                    identity.TemporaryWeakClientAccessToken, odinContext, cn);

                var unencryptedCat = ClientAccessToken.FromPortableBytes(keyStoreKey);
                var rawIcrKey = odinContext.PermissionsContext.GetIcrKey();
                var encryptedCat = EncryptedClientAccessToken.Encrypt(rawIcrKey, unencryptedCat);

                identity.EncryptedClientAccessToken = encryptedCat;
                identity.TemporaryWeakClientAccessToken = null;

                SaveIcr(identity, odinContext, cn);
            }
        }

        private async Task UpgradeKeyStoreKeyEncryptionIfNeeded(IdentityConnectionRegistration identity, IOdinContext odinContext, DatabaseConnection cn)
        {
            if (identity.AccessGrant.MasterKeyEncryptedKeyStoreKey == null)
            {
                logger.LogDebug("Upgrading KSK Encryption for {id}", identity.OdinId);

                var keyStoreKey = await publicPrivateKeyService.EccDecryptPayload(
                    PublicPrivateKeyType.OnlineIcrEncryptedKey,
                    identity.TempWeakKeyStoreKey, odinContext, cn);

                var masterKey = odinContext.Caller.GetMasterKey();
                identity.AccessGrant.MasterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(masterKey, new SensitiveByteArray(keyStoreKey));
                identity.TempWeakKeyStoreKey = null;
                
                SaveIcr(identity, odinContext, cn);
            }
        }
    }
}