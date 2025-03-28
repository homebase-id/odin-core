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
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Util;
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
        SharedKeyedAsyncLock<CircleNetworkService> keyedAsyncLock,
        ExchangeGrantService exchangeGrantService,
        TenantContext tenantContext,
        IAppRegistrationService appRegistrationService,
        CircleMembershipService circleMembershipService,
        IMediator mediator,
        CircleDefinitionService circleDefinitionService,
        DriveManager driveManager,
        PublicPrivateKeyService publicPrivateKeyService,
        CircleNetworkStorage circleNetworkStorage,
        IdentityDatabase db)
        : INotificationHandler<DriveDefinitionAddedNotification>,
            INotificationHandler<AppRegistrationChangedNotification>
    {
        /// <summary>
        /// Creates a <see cref="PermissionContext"/> for the specified caller based on their access
        /// </summary>
        public async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreateTransitPermissionContextAsync(
            OdinId odinId,
            ClientAuthenticationToken remoteIcrToken,
            IOdinContext odinContext)
        {
            logger.LogDebug("Creating transit permission context for [{odinId}]", odinId);

            var icr = await this.GetIcrAsync(odinId, remoteIcrToken);

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

            var (permissionContext, enabledCircles) = await CreatePermissionContextInternalAsync(
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
        public async Task<IOdinContext> TryCreateConnectedYouAuthContextAsync(OdinId odinId, ClientAuthenticationToken authToken,
            AccessRegistration accessReg,
            IOdinContext odinContext)
        {
            logger.LogDebug("TryCreateConnectedYouAuthContext for {id}", odinId);

            var icr = await GetIdentityConnectionRegistrationInternalAsync(odinId);
            bool isValid = icr.AccessGrant?.IsValid() ?? false;
            bool isConnected = icr.IsConnected();

            if (icr.Status == ConnectionStatus.Blocked)
            {
                return null;
            }

            // Only return the permissions if the identity is connected.
            if (isValid && isConnected)
            {
                var (permissionContext, enabledCircles) = await CreatePermissionContextInternalAsync(
                    icr: icr,
                    authToken: authToken,
                    accessReg: accessReg,
                    applyAppCircleGrants: false,
                    odinContext: odinContext);


                var transientTempDrive = SystemDriveConstants.TransientTempDrive;
                var transientTempDriveGrant = new DriveGrant()
                {
                    DriveId = (await driveManager.GetDriveIdByAliasAsync(transientTempDrive)).GetValueOrDefault(),
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
        public async Task<bool> DisconnectAsync(OdinId odinId, IOdinContext odinContext)
        {
            odinContext.AssertCanManageConnections();

            var info = await this.GetIcrAsync(odinId, odinContext);
            if (info is { Status: ConnectionStatus.Connected })
            {
                await circleNetworkStorage.DeleteAsync(odinId);

                await mediator.Publish(new ConnectionDeletedNotification()
                {
                    OdinContext = odinContext,
                    OdinId = odinId,
                });

                return true;
            }

            return false;
        }

        /// <summary>
        /// Blocks the specified <see cref="OdinId"/> from your network
        /// </summary>
        public async Task<bool> BlockAsync(OdinId odinId, IOdinContext odinContext)
        {
            odinContext.AssertCanManageConnections();

            var info = await this.GetIcrAsync(odinId, odinContext);

            //TODO: when you block a connection, you must also destroy exchange grant

            if (info.Status == ConnectionStatus.Connected)
            {
                info.Status = ConnectionStatus.Blocked;
                info.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await this.SaveIcrAsync(info, odinContext);

                await mediator.Publish(new ConnectionBlockedNotification
                {
                    OdinContext = odinContext,
                    OdinId = odinId,
                });

                return true;
            }

            if (info.Status == ConnectionStatus.Blocked || info.Status == ConnectionStatus.None)
            {
                info.Status = ConnectionStatus.Blocked;
                info.Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                info.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await this.SaveIcrAsync(info, odinContext);

                await mediator.Publish(new ConnectionBlockedNotification
                {
                    OdinContext = odinContext,
                    OdinId = odinId,
                });


                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets profiles that have been marked as <see cref="ConnectionStatus.Blocked"/>
        /// </summary>
        public async Task<CursoredResult<IdentityConnectionRegistration>> GetBlockedProfilesAsync(int count, string cursor,
            IOdinContext odinContext)
        {
            return await GetConnectionsInternalAsync(count, cursor, ConnectionStatus.Blocked, odinContext);
        }

        /// <summary>
        /// Returns a list of identities which are connected to this DI
        /// </summary>
        public async Task<CursoredResult<IdentityConnectionRegistration>> GetConnectedIdentitiesAsync(int count, string cursor,
            IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
            return await GetConnectionsInternalAsync(count, cursor, ConnectionStatus.Connected, odinContext);
        }

        /// <summary>
        /// Unblocks the specified <see cref="OdinId"/> from your network
        /// </summary>
        public async Task<bool> UnblockAsync(OdinId odinId, IOdinContext odinContext)
        {
            odinContext.AssertCanManageConnections();

            var info = await this.GetIcrAsync(odinId, odinContext);
            if (info.Status == ConnectionStatus.Blocked)
            {
                bool isValid = info.AccessGrant?.IsValid() ?? false;

                if (isValid)
                {
                    info.Status = ConnectionStatus.Connected;
                    await this.SaveIcrAsync(info, odinContext);
                    return true;
                }

                info.Status = ConnectionStatus.None;
                await this.SaveIcrAsync(info, odinContext);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the current connection info
        /// </summary>
        /// <returns></returns>
        public async Task<IdentityConnectionRegistration> GetIcrAsync(OdinId odinId, IOdinContext odinContext,
            bool overrideHack = false,
            bool tryUpgradeEncryption = true)
        {
            //TODO: need to cache here?
            //HACK: DOING THIS WHILE DESIGNING x-token - REMOVE THIS
            if (!overrideHack)
            {
                odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
            }

            var icr = await GetIdentityConnectionRegistrationInternalAsync(odinId);

            // 
            if (tryUpgradeEncryption)
            {
                await this.UpgradeTokenEncryptionIfNeededAsync(icr, odinContext);
                icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
            }

            return icr;
        }

        /// <summary>
        /// Gets the connection info if the specified <param name="remoteClientAuthenticationToken">x-token half key</param> is valid
        /// </summary>
        public async Task<IdentityConnectionRegistration> GetIcrAsync(
            OdinId odinId,
            ClientAuthenticationToken remoteClientAuthenticationToken)
        {
            var connection = await GetIdentityConnectionRegistrationInternalAsync(odinId);

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
        public async Task<bool> IsConnectedAsync(OdinId odinId, IOdinContext odinContext)
        {
            //allow the caller to see if s/he is connected, otherwise
            if (odinContext.Caller.OdinId != odinId)
            {
                odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
            }

            var info = await this.GetIcrAsync(odinId, odinContext);
            return info.Status == ConnectionStatus.Connected;
        }

        public async Task<IEnumerable<OdinId>> GetCircleMembersAsync(GuidId circleId, IOdinContext odinContext)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadCircleMembership);
            //added override:true because PermissionKeys.ReadCircleMembership is present
            var result = (await circleMembershipService.GetDomainsInCircleAsync(circleId, odinContext, overrideHack: true))
                .Where(d => d.DomainType == DomainType.Identity)
                .Select(m => new OdinId(m.Domain));
            return result;
        }

        /// <summary>
        /// Throws an exception if the odinId is blocked.
        /// </summary>
        /// <param name="odinId"></param>
        /// <param name="odinContext"></param>
        /// <returns></returns>
        public async Task AssertConnectionIsNoneOrValidAsync(OdinId odinId, IOdinContext odinContext)
        {
            var info = await this.GetIcrAsync(odinId, odinContext);
            this.AssertConnectionIsNoneOrValid(info);
        }

        /// <summary>
        /// Adds the specified odinId to your network
        /// </summary>
        /// <returns></returns>
        public async Task ConnectAsync(string odinIdentity, AccessExchangeGrant accessGrant,
            (EncryptedClientAccessToken EncryptedCat, (EccEncryptedPayload Token, EccEncryptedPayload KeyStoreKey) Temp) keys,
            ContactRequestData contactData,
            ConnectionRequestOrigin connectionRequestOrigin,
            OdinId? introducerOdinId,
            byte[] verificationHash,
            IOdinContext odinContext)
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
                EncryptedClientAccessToken = keys.EncryptedCat, //may come in as NULL; meaning this cannot be used until we have the ICR key
                TemporaryWeakClientAccessToken = keys.Temp.Token,
                TempWeakKeyStoreKey = keys.Temp.KeyStoreKey,
                ConnectionRequestOrigin = connectionRequestOrigin,
                IntroducerOdinId = introducerOdinId,
                VerificationHash = verificationHash
            };

            await this.SaveIcrAsync(newConnection, odinContext);

            await mediator.Publish(new ConnectionFinalizedNotification()
            {
                OdinId = odinId,
                OdinContext = odinContext,
            });
        }

        /// <summary>
        /// Gives access to all resource granted by the specified circle to the odinId
        /// </summary>
        public async Task GrantCircleAsync(GuidId circleId, OdinId odinId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);

            if (icr == null || !icr.IsConnected())
            {
                throw new OdinSecurityException($"{odinId} must have valid connection to be added to a circle");
            }

            if (icr.AccessGrant.CircleGrants.TryGetValue(SystemCircleConstants.AutoConnectionsCircleId, out _))
            {
                throw new OdinClientException(
                    $"Cannot grant additional circles to auto-connected identity.  You must first confirm the connection.",
                    OdinClientErrorCode.CannotGrantAutoConnectedMoreCircles);
            }

            if (icr.AccessGrant.CircleGrants.TryGetValue(circleId, out _))
            {
                //TODO: Here we should ensure it's in the _circleMemberStorage just in case this was called because it's out of sync
                throw new OdinClientException($"{odinId} is already member of circle", OdinClientErrorCode.IdentityAlreadyMemberOfCircle);
            }

            var circleDefinition = await circleMembershipService.GetCircleAsync(circleId, odinContext);
            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            var circleGrant = await circleMembershipService.CreateCircleGrantAsync(keyStoreKey, circleDefinition, masterKey, odinContext);

            icr.AccessGrant.CircleGrants.Add(circleGrant.CircleId, circleGrant);

            //
            // Check the apps.  If the circle being granted is authorized by an app
            // ensure the new member gets the permissions given by the app
            //
            var allApps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
            var appsThatGrantThisCircle = allApps.Where(reg => reg?.AuthorizedCircles?.Any(c => c == circleId) ?? false);

            foreach (var app in appsThatGrantThisCircle)
            {
                var appCircleGrant = await this.CreateAppCircleGrantAsync(app, keyStoreKey, circleId, masterKey);
                icr.AccessGrant.AddUpdateAppCircleGrant(appCircleGrant);
            }

            keyStoreKey.Wipe();
            await this.SaveIcrAsync(icr, odinContext);
        }

        /// <summary>
        /// Removes drives and permissions of the specified circle from the odinId
        /// </summary>
        public async Task RevokeCircleAccessAsync(GuidId circleId, OdinId odinId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
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

            await this.SaveIcrAsync(icr, odinContext);
        }

        public async Task<Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>> CreateAppCircleGrantListWithSystemCircle(
            SensitiveByteArray keyStoreKey,
            List<GuidId> circleIds,
            ConnectionRequestOrigin origin,
            SensitiveByteArray masterKey,
            IOdinContext odinContext)
        {
            var list = CircleNetworkUtils.EnsureSystemCircles(circleIds, origin);
            return await this.CreateAppCircleGrantList(keyStoreKey, list, masterKey, odinContext);
        }


        public async Task<Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>> CreateAppCircleGrantList(
            SensitiveByteArray keyStoreKey,
            List<GuidId> circleIds,
            SensitiveByteArray masterKey,
            IOdinContext odinContext)
        {
            var appGrants = new Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>();

            foreach (var circleId in circleIds)
            {
                var appsThatGrantThisCircle = await appRegistrationService.GetAppsGrantingCircleAsync(circleId, odinContext);

                foreach (var app in appsThatGrantThisCircle)
                {
                    var appKey = app.AppId.Value;
                    var appCircleGrant = await this.CreateAppCircleGrantAsync(app, keyStoreKey, circleId, masterKey);

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
        public async Task UpdateCircleDefinitionAsync(CircleDefinition circleDef, IOdinContext odinContext)
        {
            await circleMembershipService.AssertValidDriveGrantsAsync(circleDef.DriveGrants);

            var members = await GetCircleMembersAsync(circleDef.Id, odinContext);
            var masterKey = odinContext.Caller.GetMasterKey();

            // List<OdinId> invalidMembers = new List<OdinId>();
            foreach (var odinId in members)
            {
                var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);

                var circleKey = circleDef.Id;
                var hasCg = icr.AccessGrant.CircleGrants.Remove(circleKey, out _);

                if (icr.IsConnected() && hasCg)
                {
                    if (icr.AccessGrant == null)
                    {
                        logger.LogError("icr for {odinID} has null access grant", odinId);
                    }

                    await UpgradeTokenEncryptionIfNeededAsync(icr, odinContext);
                    if (await UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync(icr, odinContext))
                    {
                        // refetch the record since the above method just writes to db
                        icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
                    }

                    // Re-create the circle grant so
                    var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
                    icr.AccessGrant.CircleGrants[circleKey] =
                        await circleMembershipService.CreateCircleGrantAsync(keyStoreKey, circleDef, masterKey, odinContext);
                    keyStoreKey.Wipe();
                }
                else
                {
                    //It should not occur that a circle has a member
                    //who is not connected but let's capture i
                    // invalidMembers.Add(odinId);
                }

                await this.SaveIcrAsync(icr, odinContext);
            }

            await circleMembershipService.UpdateAsync(circleDef, odinContext);

            //TODO: determine how to handle invalidMembers - do we return to the UI?  do we remove from all circles?
        }

        public async Task<List<OdinId>> GetInvalidMembersOfCircleDefinition(CircleDefinition circleDef, IOdinContext odinContext)
        {
            await circleMembershipService.AssertValidDriveGrantsAsync(circleDef.DriveGrants);

            var members = await GetCircleMembersAsync(circleDef.Id, odinContext);

            var invalid = new List<OdinId>();
            foreach (var odinId in members)
            {
                var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
                var hasCg = icr.AccessGrant.CircleGrants.TryGetValue(circleDef.Id, out var cg);

                if (icr.IsConnected() && !hasCg)
                {
                    invalid.Add(icr.OdinId);
                }
            }

            return invalid;
        }

        /// <summary>
        /// Tests if a circle has members and indicates if it can be deleted
        /// </summary>
        public async Task DeleteCircleDefinitionAsync(GuidId circleId, IOdinContext odinContext)
        {
            var members = await this.GetCircleMembersAsync(circleId, odinContext);

            if (members.Any())
            {
                throw new OdinClientException("Cannot delete a circle with members", OdinClientErrorCode.CannotDeleteCircleWithMembers);
            }

            await circleMembershipService.DeleteAsync(circleId, odinContext);
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

        public async Task RevokeConnectionAsync(OdinId odinId, IOdinContext odinContext)
        {
            await circleNetworkStorage.DeleteAsync(odinId);
            await mediator.Publish(new ConnectionDeletedNotification()
            {
                OdinId = odinId,
                OdinContext = odinContext,
            });
        }

        public async Task<IcrTroubleshootingInfo> GetTroubleshootingInfoAsync(OdinId odinId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            // Need to see if the circle has the correct drives
            //
            // SystemCircleConstants.ConnectedIdentitiesSystemCircleInitialDrives

            var info = new IcrTroubleshootingInfo();
            var circleDefinitions = (await circleDefinitionService.GetCirclesAsync(true)).ToList();
            var icr = await GetIdentityConnectionRegistrationInternalAsync(odinId);

            info.Icr = icr.Redacted();

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
                        var driveId = await driveManager.GetDriveIdByAliasAsync(expectedDriveGrant.PermissionedDrive.Drive);
                        var driveInfo = await driveManager.GetDriveAsync(driveId.GetValueOrDefault());

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
            IOdinContext odinContext)
        {
            var masterKey = odinContext.Caller.GetMasterKey();
            var appKey = newAppRegistration.AppId.Value;

            await using var tx = await db.BeginStackedTransactionAsync();

            if (null != oldAppRegistration)
            {
                var circlesToRevoke = oldAppRegistration.AuthorizedCircles.Except(newAppRegistration.AuthorizedCircles);
                //TODO: spin thru circles to revoke an update members

                foreach (var circleId in circlesToRevoke)
                {
                    //get all circle members and update their grants
                    var members = await this.GetCircleMembersAsync(circleId, odinContext);

                    foreach (var odinId in members)
                    {
                        var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
                        icr.AccessGrant.AppGrants[appKey]?.Remove(circleId);
                        await this.SaveIcrAsync(icr, odinContext);
                    }
                }
            }

            foreach (var circleId in newAppRegistration.AuthorizedCircles ?? new List<Guid>())
            {
                //get all circle members and update their grants
                var members = await this.GetCircleMembersAsync(circleId, odinContext);

                foreach (var odinId in members)
                {
                    var icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
                    if (await UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync(icr, odinContext))
                    {
                        // refetch the record since the above method just writes to db
                        icr = await this.GetIdentityConnectionRegistrationInternalAsync(odinId);
                    }

                    if (!icr.AccessGrant.AppGrants.TryGetValue(appKey, out var appCircleGrantDictionary))
                    {
                        appCircleGrantDictionary = new Dictionary<Guid, AppCircleGrant>();
                    }

                    var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
                    var appCircleGrant = await this.CreateAppCircleGrantAsync(newAppRegistration, keyStoreKey, circleId, masterKey);
                    appCircleGrantDictionary[appCircleGrant.CircleId] = appCircleGrant;
                    keyStoreKey.Wipe();

                    icr.AccessGrant.AppGrants[appKey] = appCircleGrantDictionary;
                    await this.SaveIcrAsync(icr, odinContext);
                }
            }
            //

            tx.Commit();
        }

        public async Task<VerifyConnectionResponse> GetCallerVerificationHashAsync(IOdinContext odinContext)
        {
            if (!odinContext.Caller.IsConnected)
            {
                logger.LogDebug("Verification Connection Code - not connected, " +
                                "returning null hash.(AuthContext:{ac})",
                    odinContext.AuthContext);

                return new VerifyConnectionResponse
                {
                    IsConnected = false,
                    Hash = null
                };
            }

            //look up the verification hash on the caller's icr
            var callerIcr = await this.GetIcrAsync(odinContext.GetCallerOdinIdOrFail(), odinContext, true);

            if (callerIcr.VerificationHash.IsNullOrEmpty())
            {
                throw new OdinSecurityException("Cannot verify caller");
            }

            var result = new VerifyConnectionResponse()
            {
                IsConnected = callerIcr.IsConnected(),
                Hash = callerIcr.VerificationHash
            };

            return result;
        }

        /// <summary>
        /// Upgrades a connection which was created automatically (i.e. because of an introduction) to a confirmed connection
        /// </summary>
        public async Task ConfirmConnectionAsync(OdinId odinId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var icr = await this.GetIcrAsync(odinId, odinContext);

            if (!icr.IsConnected())
            {
                throw new OdinClientException("Cannot confirm identity that is not connected", OdinClientErrorCode.IdentityMustBeConnected);
            }

            if (!icr.AccessGrant.CircleGrants.TryGetValue(SystemCircleConstants.AutoConnectionsCircleId, out _))
            {
                throw new OdinClientException("Cannot confirm identity that is not in the AutoConnectionsCircle",
                    OdinClientErrorCode.NotAnAutoConnection);
            }


            await using var tx = await db.BeginStackedTransactionAsync();

            await UpgradeTokenEncryptionIfNeededAsync(icr, odinContext);
            await UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync(icr, odinContext);

            await this.RevokeCircleAccessAsync(SystemCircleConstants.AutoConnectionsCircleId, odinId, odinContext);
            await this.GrantCircleAsync(SystemCircleConstants.ConfirmedConnectionsCircleId, odinId, odinContext);

            tx.Commit();
        }

        public async Task<bool> ClearVerificationHashAsync(OdinId odinId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var icr = await this.GetIcrAsync(odinId, odinContext);

            if (!icr.VerificationHash.IsNullOrEmpty())
            {
                await circleNetworkStorage.UpdateVerificationHashAsync(icr.OdinId, icr.Status, []);
                logger.LogDebug("Hash was cleared for identity [{identity}]", icr.OdinId);
                return true;
            }

            return false;
        }


        public async Task<bool> UpdateVerificationHashAsync(OdinId odinId, Guid randomCode, SensitiveByteArray sharedSecret,
            IOdinContext odinContext)
        {
            if (!odinContext.Caller.IsOwner)
            {
                odinContext.Caller.AssertCallerIsConnected();
                OdinValidationUtils.AssertIsTrue(odinId == odinContext.GetCallerOdinIdOrFail(), "caller does not match target identity");
            }

            var icr = await this.GetIcrAsync(odinId, odinContext);

            if (!icr.IsConnected())
            {
                logger.LogDebug("Skipping UpdateVerificationHash -[{icr}] is not connected", icr.OdinId);
                return false;
            }

            // if (icr.VerificationHash.IsNullOrEmpty())
            {
                // this should not occur since this process is running at the same time
                // we introduce the ability to have a null EncryptedClientAccessToken
                // for a connected identity; but #paranoid
                if (icr.EncryptedClientAccessToken == null)
                {
                    logger.LogDebug("Skipping UpdateVerificationHash since connected identity was missing EncryptedClientAccessToken");
                    return false;
                }

                var hash = this.CreateVerificationHash(randomCode, sharedSecret);

                logger.LogDebug("Saving identity [{identity}] with hash [{hash}]", icr.OdinId, hash.ToBase64());

                await circleNetworkStorage.UpdateVerificationHashAsync(icr.OdinId, icr.Status, hash);

                return true;
            }
            //
            // logger.LogDebug("Skipping verification hash update for identity [{identity}] " +
            //                 "called but one is already set [hash:{value}]",
            //     icr.OdinId,
            //     icr.VerificationHash.ToBase64());
            //
            // return false;
        }

        public byte[] CreateVerificationHash(Guid randomCode, SensitiveByteArray sharedSecret)
        {
            var combined = ByteArrayUtil.Combine(randomCode.ToByteArray(), sharedSecret.GetKey());
            var expectedHash = ByteArrayUtil.CalculateSHA256Hash(combined);
            return expectedHash;
        }

        public async Task<ClientAccessToken> CreatePeerIcrClientForCallerAsync(IOdinContext odinContext)
        {
            odinContext.Caller.AssertCallerIsConnected();
            var caller = odinContext.GetCallerOdinIdOrFail();

            var grantKeyStoreKey = odinContext.PermissionsContext.GetKeyStoreKey();
            var (accessRegistration, token) = await exchangeGrantService.CreateClientAccessToken(
                grantKeyStoreKey, ClientTokenType.RemoteNotificationSubscriber);

            var client = new PeerIcrClient
            {
                Identity = caller,
                AccessRegistration = accessRegistration
            };

            await circleNetworkStorage.SavePeerIcrClientAsync(client);
            return token;
        }

        public async Task<PeerIcrClient> GetPeerIcrClientAsync(Guid accessRegId)
        {
            return await circleNetworkStorage.GetPeerIcrClientAsync(accessRegId);
        }

        private async Task<AppCircleGrant> CreateAppCircleGrantAsync(
            RedactedAppRegistration appReg,
            SensitiveByteArray keyStoreKey,
            GuidId circleId,
            SensitiveByteArray masterKey)
        {
            //map the exchange grant to a structure that matches ICR
            var grant = await exchangeGrantService.CreateExchangeGrantAsync(
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
            async Task UpdateIfRequired(CircleDefinition def)
            {
                var existingDriveGrant = def.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == drive.TargetDriveInfo);
                if (drive.AllowAnonymousReads == false && existingDriveGrant != null)
                {
                    //remove the drive as it no longer allows anonymous reads
                    def.DriveGrants = def.DriveGrants.Where(dg => dg.PermissionedDrive.Drive != drive.TargetDriveInfo).ToList();
                    await this.UpdateCircleDefinitionAsync(def, odinContext);
                    return;
                }

                if (drive.AllowAnonymousReads && null == existingDriveGrant)
                {
                    //act like it's new
                    await this.HandleDriveAdded(drive, odinContext);
                }
            }

            CircleDefinition confirmedCircle = await
                circleMembershipService.GetCircleAsync(SystemCircleConstants.ConfirmedConnectionsCircleId, odinContext);
            await UpdateIfRequired(confirmedCircle);

            CircleDefinition autoConnectedCircle = await
                circleMembershipService.GetCircleAsync(SystemCircleConstants.AutoConnectionsCircleId, odinContext);
            await UpdateIfRequired(autoConnectedCircle);
        }

        /// <summary>
        /// Updates the system circles drive grants
        /// </summary>
        private async Task HandleDriveAdded(StorageDrive drive, IOdinContext odinContext)
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
                await this.UpdateCircleDefinitionAsync(def, odinContext);
            }

            CircleDefinition confirmedCircle = await
                circleMembershipService.GetCircleAsync(SystemCircleConstants.ConfirmedConnectionsCircleId, odinContext);
            await GrantAnonymousRead(confirmedCircle);

            CircleDefinition autoConnectedCircle = await
                circleMembershipService.GetCircleAsync(SystemCircleConstants.AutoConnectionsCircleId, odinContext);
            await GrantAnonymousRead(autoConnectedCircle);
        }


        private async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreatePermissionContextInternalAsync(
            IdentityConnectionRegistration icr,
            ClientAuthenticationToken authToken,
            AccessRegistration accessReg,
            bool applyAppCircleGrants,
            IOdinContext odinContext)
        {
            // Note: the icr.AccessGrant.AccessRegistration and parameter accessReg might not be the same in the case of YouAuth; this is intentional


            var (grants, enabledCircles) = await
                circleMembershipService.MapCircleGrantsToExchangeGrantsAsync(icr.OdinId.AsciiDomain,
                    icr.AccessGrant.CircleGrants.Values.ToList(), odinContext);

            if (applyAppCircleGrants)
            {
                foreach (var kvp in icr.AccessGrant.AppGrants)
                {
                    // var appId = kvp.Key;
                    var appCircleGrantDictionary = kvp.Value;

                    foreach (var (_, appCg) in appCircleGrantDictionary)
                    {
                        var alreadyEnabledCircle = enabledCircles.Exists(cid => cid == appCg.CircleId);
                        if (alreadyEnabledCircle || await circleDefinitionService.IsEnabledAsync(appCg.CircleId))
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

                                    logger.LogDebug(message);
                                }
                                else
                                {
                                    logger.LogDebug(
                                        $"Wild; so wild. grants.ContainsKey says it has {kvp.Key} but grants.TryGetValues does not???");
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
            var feedDriveWriteGrant = await exchangeGrantService.CreateExchangeGrantAsync(keyStoreKey, new Permissions_PermissionSet(),
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
                additionalPermissionKeys: permissionKeys,
                includeAnonymousDrives: true,
                anonymousDrivePermission: anonDrivePermissions);

            var result = (permissionCtx, enabledCircles);
            return result;
        }


        private async Task<CursoredResult<IdentityConnectionRegistration>> GetConnectionsInternalAsync(int count, string cursor,
            ConnectionStatus status,
            IOdinContext odinContext)
        {
            var (list, nextCursor) = await circleNetworkStorage.GetListAsync(count, cursor, status);
            return new CursoredResult<IdentityConnectionRegistration>()
            {
                Cursor = nextCursor,
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

        private async Task<IdentityConnectionRegistration> GetIdentityConnectionRegistrationInternalAsync(OdinId odinId)
        {
            var registration = await circleNetworkStorage.GetAsync(odinId);

            if (null == registration)
            {
                return new IdentityConnectionRegistration()
                {
                    OdinId = odinId,
                    Status = ConnectionStatus.None,
                    LastUpdated = -1
                };
            }

            return registration;
        }

        private async Task SaveIcrAsync(IdentityConnectionRegistration icr, IOdinContext odinContext)
        {
            // SEB:TODO does not scale
            // SEB:TODO delete all these debug logs when we we have gotten rid of the keyed lock
            logger.LogDebug("SaveIcrAsync -> Acquiring");
            using (await keyedAsyncLock.LockAsync(icr.OdinId))
            {
                //TODO: this is a critical change; need to audit this
                if (icr.Status == ConnectionStatus.None)
                {
                    logger.LogDebug("SaveIcrAsync -> before DeleteAsync");
                    await circleNetworkStorage.DeleteAsync(icr.OdinId);
                    logger.LogDebug("SaveIcrAsync -> after DeleteAsync");
                }
                else
                {
                    logger.LogDebug("SaveIcrAsync -> before UpsertAsync");
                    await circleNetworkStorage.UpsertAsync(icr, odinContext);
                    logger.LogDebug("SaveIcrAsync -> after UpsertAsync");
                }

                logger.LogDebug("SaveIcrAsync -> Releasing");
            }
        }

        public async Task UpgradeTokenEncryptionIfNeededAsync(IdentityConnectionRegistration identity, IOdinContext odinContext)
        {
            if (identity.TemporaryWeakClientAccessToken != null && identity.EncryptedClientAccessToken == null)
            {
                logger.LogDebug("Upgrading ICR Token Encryption for {id}", identity.OdinId);

                var keyStoreKey = await publicPrivateKeyService.EccDecryptPayload(identity.TemporaryWeakClientAccessToken, odinContext);

                var unencryptedCat = ClientAccessToken.FromPortableBytes(keyStoreKey);
                var rawIcrKey = odinContext.PermissionsContext.GetIcrKey();
                var encryptedCat = EncryptedClientAccessToken.Encrypt(rawIcrKey, unencryptedCat);

                await circleNetworkStorage.UpdateClientAccessTokenAsync(identity.OdinId, identity.Status, encryptedCat);
            }
        }

        private async Task<bool> UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync(IdentityConnectionRegistration identity,
            IOdinContext odinContext)
        {
            if (identity.AccessGrant.RequiresMasterKeyEncryptionUpgrade())
            {
                logger.LogDebug("Upgrading KSK Encryption for {id}", identity.OdinId);

                var keyStoreKey = await publicPrivateKeyService.EccDecryptPayload(identity.TempWeakKeyStoreKey, odinContext);

                var masterKey = odinContext.Caller.GetMasterKey();
                var masterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(masterKey, new SensitiveByteArray(keyStoreKey));
                await circleNetworkStorage.UpdateKeyStoreKeyAsync(identity.OdinId, identity.Status, masterKeyEncryptedKeyStoreKey);
                return true;
            }

            return false;
        }
    }
}