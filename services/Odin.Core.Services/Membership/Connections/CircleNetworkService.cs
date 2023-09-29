using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Mediator;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Core.Services.Membership.Circles;
using Odin.Core.Services.Membership.Connections.Requests;
using Odin.Core.Time;
using PermissionSet = Odin.Core.Services.Authorization.Permissions.PermissionSet;

namespace Odin.Core.Services.Membership.Connections
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
        private readonly CircleMembershipService _circleMembershipService;
        private readonly TenantContext _tenantContext;
        private readonly IAppRegistrationService _appRegistrationService;
        private readonly IMediator _mediator;
        private readonly CircleDefinitionService _circleDefinitionService;

        public CircleNetworkService(OdinContextAccessor contextAccessor,
            ExchangeGrantService exchangeGrantService, TenantContext tenantContext,
            IAppRegistrationService appRegistrationService, TenantSystemStorage tenantSystemStorage, CircleMembershipService circleMembershipService,
            IMediator mediator, CircleDefinitionService circleDefinitionService)
        {
            _contextAccessor = contextAccessor;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _appRegistrationService = appRegistrationService;
            _circleMembershipService = circleMembershipService;
            _mediator = mediator;
            _circleDefinitionService = circleDefinitionService;

            _storage = new CircleNetworkStorage(tenantSystemStorage, circleMembershipService);
        }

        /// <summary>
        /// Creates a <see cref="PermissionContext"/> for the specified caller based on their access
        /// </summary>
        public async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreateTransitPermissionContext(OdinId odinId,
            ClientAuthenticationToken remoteIcrToken)
        {
            var icr = await this.GetIdentityConnectionRegistration(odinId, remoteIcrToken);

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
                applyAppCircleGrants: true);

            return (permissionContext, enabledCircles);
        }

        /// <summary>
        /// Tries to create caller and permission context for the given OdinId if is connected
        /// </summary>
        public async Task<OdinContext> TryCreateConnectedYouAuthContext(OdinId odinId, ClientAuthenticationToken authToken, AccessRegistration accessReg)
        {
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
                    applyAppCircleGrants: false);

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
        /// <param name="odinId"></param>
        /// <returns></returns>
        public async Task<bool> Disconnect(OdinId odinId)
        {
            _contextAccessor.GetCurrent().AssertCanManageConnections();

            var info = await this.GetIdentityConnectionRegistration(odinId);
            if (info is { Status: ConnectionStatus.Connected })
            {
                _storage.Delete(odinId);

                await _mediator.Publish(new IdentityConnectionRegistrationChangedNotification()
                {
                    OdinId = odinId
                });

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
            //HACK: DOING THIS WHILE DESIGNING x-token - REMOVE THIS
            if (!overrideHack)
            {
                _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnections);
            }

            return await GetIdentityConnectionRegistrationInternal(odinId);
        }

        /// <summary>
        /// Gets the connection info if the specified <param name="remoteClientAuthenticationToken">x-token half key</param> is valid
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
            var result = _circleMembershipService.GetDomainsInCircle(circleId).Where(d => d.DomainType == DomainType.Identity)
                .Select(m => new OdinId(m.Domain));
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
        /// Adds the specified odinId to your network
        /// </summary>
        /// <param name="odinIdentity">The public key certificate containing the domain name which will be connected</param>
        /// <param name="accessGrant">The access to be given to this connection</param>
        /// <param name="encryptedCat">The keys used when accessing the remote identity</param>
        /// <param name="contactData"></param>
        /// <returns></returns>
        public Task Connect(string odinIdentity, AccessExchangeGrant accessGrant, EncryptedClientAccessToken encryptedCat, ContactRequestData contactData)
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

            this.SaveIcr(newConnection);
            return Task.CompletedTask;
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

            var circleDefinition = _circleMembershipService.GetCircle(circleId);
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
            var circleGrant = await _circleMembershipService.CreateCircleGrant(circleDefinition, keyStoreKey, masterKey);

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
            foreach (var (_, appCircleGrants) in icr.AccessGrant.AppGrants)
            {
                appCircleGrants.Remove(circleId.Value);
            }

            this.SaveIcr(icr);
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
        /// <param name="circleDef"></param>
        public async Task UpdateCircleDefinition(CircleDefinition circleDef)
        {
            Guard.Argument(circleDef, nameof(circleDef)).NotNull();

            _circleMembershipService.AssertValidDriveGrants(circleDef.DriveGrants);

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
                    icr.AccessGrant.CircleGrants[circleKey] = await _circleMembershipService.CreateCircleGrant(circleDef, keyStoreKey, masterKey);
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

            await _circleMembershipService.Update(circleDef);

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

            await _circleMembershipService.Delete(circleId);
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

        public async Task RevokeConnection(OdinId odinId)
        {
            _storage.Delete(odinId);
            await _mediator.Publish(new IdentityConnectionRegistrationChangedNotification()
            {
                OdinId = odinId
            });
        }

        //

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
            CircleDefinition systemCircle = _circleMembershipService.GetCircle(CircleConstants.ConnectedIdentitiesSystemCircleId);

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

            CircleDefinition def = _circleMembershipService.GetCircle(CircleConstants.ConnectedIdentitiesSystemCircleId);

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


        private async Task<(PermissionContext permissionContext, List<GuidId> circleIds)> CreatePermissionContextInternal(
            IdentityConnectionRegistration icr,
            ClientAuthenticationToken authToken,
            AccessRegistration accessReg,
            bool applyAppCircleGrants)
        {
            var (grants, enabledCircles) = _circleMembershipService.MapCircleGrantsToExchangeGrants(icr.AccessGrant.CircleGrants.Values.ToList());

            if (applyAppCircleGrants)
            {
                foreach (var kvp in icr.AccessGrant.AppGrants)
                {
                    // var appId = kvp.Key;
                    var appCircleGrantDictionary = kvp.Value;

                    foreach (var (_, appCg) in appCircleGrantDictionary)
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

            //TODO: only add this if I follow this identity and this is for transit
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var feedDriveWriteGrant = await _exchangeGrantService.CreateExchangeGrant(keyStoreKey, new PermissionSet(), new List<DriveGrantRequest>()
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

            var permissionKeys = _tenantContext.Settings.GetAdditionalPermissionKeysForConnectedIdentities();
            var anonDrivePermissions = _tenantContext.Settings.GetAnonymousDrivePermissionsForConnectedIdentities();

            var permissionCtx = await _exchangeGrantService.CreatePermissionContext(
                authToken: authToken,
                grants: grants,
                accessReg: accessReg,
                additionalPermissionKeys: permissionKeys,
                includeAnonymousDrives: true,
                anonymousDrivePermission: anonDrivePermissions);

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