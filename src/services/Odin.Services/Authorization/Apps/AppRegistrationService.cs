#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Apps;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version12tov13;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Util;


namespace Odin.Services.Authorization.Apps
{
    public class AppRegistrationService(
        ExchangeGrantService exchangeGrantService,
        TenantContext tenantContext,
        IMediator mediator,
        IcrKeyService icrKeyService,
        ILogger<AppRegistrationService> logger,
        IdentityDatabase db,
        ClientRegistrationStorage clientRegistrationStorage,
        LegacyDefinitionStore legacyStore,
        OdinContextCache cache,
        CircleDefinitionService circleDefinitionService,
        IDriveManager driveManager)
        : IAppRegistrationService
    {

        public async Task<RedactedAppRegistration> RegisterAppAsync(AppRegistrationRequest request, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            if (!string.IsNullOrEmpty(request.CorsHostName))
            {
                AppUtil.AssertValidCorsHeader(request.CorsHostName);
            }

            var appSlug = await AssignSlugAsync(request.AppId, request.AppSlug);

            // Registering again would replace the app's keys and cut off its devices (#2039).  Changing what it may do
            // has its own calls, and starting over with new keys is uninstalling it first.
            if (await GetAppRegistrationInternalAsync(request.AppId) != null)
            {
                throw AlreadyRegistered(request.AppId);
            }

            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var (drives, icrKey) = await GrantInputsAsync(request.PermissionSet, request.Drives, odinContext);

            // Before anything is written: the install screen is shown once, so a declared circle refused after
            // the save would never be offered again (#1939).
            var declaredCircles = request.Circles ?? [];
            await circleDefinitionService.AssertDeclaredCirclesAsync(declaredCircles, request.AppId, drives,
                request.PermissionSet!);

            var appGrant = await exchangeGrantService.CreateExchangeGrantAsync(
                keyStoreKey, request.PermissionSet!, drives, new MasterKeyStorageKeySource(masterKey), masterKey, icrKey);

            //TODO: add check to ensure app name is unique

            var appReg = new AppRegistration()
            {
                AppId = request.AppId,
                AppSlug = appSlug,
                Name = request.Name,
                AppKeyStore = appGrant,

                CorsHostName = request.CorsHostName,
                CircleMemberPermissionGrant = request.CircleMemberPermissionGrant,
                AuthorizedCircles = request.AuthorizedCircles
            };

            List<Guid> createdCircles;
            await using (var tx = await db.BeginStackedTransactionAsync())
            {
                // Insert only: a registration made since the checks above is refused by the database, not replaced.
                // Either unique key refuses it, so say which.
                if (!await db.AppRegistrations.TryInsertAsync(ToRecord(appReg)))
                {
                    throw await GetAppRegistrationInternalAsync(request.AppId) != null
                        ? AlreadyRegistered(request.AppId)
                        : new OdinClientException($"The app slug '{appSlug}' was just taken by another app",
                            OdinClientErrorCode.IdAlreadyExists);
                }

                // After the save: a circle's owning app must exist.
                createdCircles = await circleDefinitionService.CreateDeclaredCirclesAsync(declaredCircles, request.AppId);
                tx.Commit();
            }

            foreach (var circleId in createdCircles)
            {
                await mediator.Publish(new CircleDefinitionChangedNotification
                {
                    OdinContext = odinContext,
                    CircleId = circleId,
                    Change = CircleDefinitionChangeType.Created,
                });
            }

            await NotifyAppChanged(null, appReg, odinContext);
            return appReg.Redacted();
        }

        public async Task UpdateAppPermissionsAsync(UpdateAppPermissionsRequest request, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternalAsync(request.AppId);
            if (null == appReg)
            {
                throw new OdinClientException("Invalid AppId", OdinClientErrorCode.AppNotRegistered);
            }

            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = appReg.AppKeyStore.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            var (drives, icrKey) = await GrantInputsAsync(request.PermissionSet, request.Drives, odinContext);

            // The same key store key, which the app's devices hold, kept as it is stored rather than locked again
            var lockedKeyStoreKey = appReg.AppKeyStore.MasterKeyEncryptedKeyStoreKey;
            appReg.AppKeyStore = await exchangeGrantService.CreateExchangeGrantAsync(keyStoreKey, request.PermissionSet!, drives,
                new MasterKeyStorageKeySource(masterKey), masterKey: null, icrKey);
            appReg.AppKeyStore.MasterKeyEncryptedKeyStoreKey = lockedKeyStoreKey;

            await SaveAsync(appReg);

            await ResetAppPermissionContextCacheAsync();
        }

        public async Task UpdateAuthorizedCirclesAsync(UpdateAuthorizedCirclesRequest request, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var oldRegistration = await this.GetAppRegistrationInternalAsync(request.AppId);
            if (null == oldRegistration)
            {
                throw new OdinClientException("Invalid AppId", OdinClientErrorCode.AppNotRegistered);
            }

            if (request.AppId == SystemAppConstants.FeedAppId)
            {
                foreach (var cid in SystemAppConstants.FeedAppRegistrationRequest.AuthorizedCircles)
                {
                    request.AuthorizedCircles.EnsureItem(cid);
                }
            }

            var updatedAppReg = new AppRegistration()
            {
                AppId = oldRegistration.AppId,
                AppSlug = oldRegistration.AppSlug, // immutable; other identities address the app by it
                Name = oldRegistration.Name,
                AppKeyStore = oldRegistration.AppKeyStore,
                CorsHostName = oldRegistration.CorsHostName,
                ZkMasterKeyEncryptedAppKey = oldRegistration.ZkMasterKeyEncryptedAppKey,
                ZkAppKeyEncryptedStorageKeys = oldRegistration.ZkAppKeyEncryptedStorageKeys,

                CircleMemberPermissionGrant = request.CircleMemberPermissionGrant,
                AuthorizedCircles = request.AuthorizedCircles
            };

            await SaveAsync(updatedAppReg);

            //TODO: consider optimize by checking if anything actually changed before calling notify app changed

            await NotifyAppChanged(oldRegistration, updatedAppReg, odinContext);
            await ResetAppPermissionContextCacheAsync();
        }

        public async Task<(ClientAccessToken cat, string corsHostName)> RegisterClientAsync(GuidId appId, string friendlyName,
            IOdinContext odinContext, string? zkAppKeyId = null)
        {
            odinContext.Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternalAsync(appId);
            if (appReg == null)
            {
                throw new OdinClientException("App must be registered to add a client", OdinClientErrorCode.AppNotRegistered);
            }

            var masterKey = odinContext.Caller.GetMasterKey();
            var (accessRegistration, cat) =
                await exchangeGrantService.CreateClientAccessToken(appReg.AppKeyStore, masterKey, ClientTokenType.App);

            var appClient = new AppClientRegistration(odinContext.Tenant, appId, friendlyName, accessRegistration)
            {
                ZkAppKeyId = zkAppKeyId
            };
            await SaveClientAsync(appClient);
            return (cat, appReg.CorsHostName);
        }

        public async Task<RedactedAppRegistration?> GetAppRegistration(GuidId appId, IOdinContext odinContext)
        {
            var result = await GetAppRegistrationInternalAsync(appId);
            return result?.Redacted();
        }

        public async Task<RedactedAppRegistration> GetRegisteredAppOrThrowAsync(GuidId appId, IOdinContext odinContext)
        {
            return await GetAppRegistration(appId, odinContext)
                   ?? throw new OdinClientException($"No app is registered with id {appId}",
                       OdinClientErrorCode.AppNotRegistered);
        }

        /// <summary>
        /// The app addressed as <c>{appSlug}</c> -- the first half of
        /// <c>/apps/{appSlug}/drives/{driveSlug}</c>.  Null when no app holds that slug.
        /// </summary>
        /// <remarks>
        /// Served by UNIQUE(identityId, AppSlug), so the result is single by construction.
        ///
        /// No pre-v13 fallback, unlike <see cref="GetAppRegistrationInternalAsync"/>: slugs are a
        /// column and only exist on rows, so a legacy blob registration has nothing to match on.  An
        /// identity that has not run the v13 migration simply has no slug-addressable apps, which is
        /// the truth rather than a gap.
        /// </remarks>
        public async Task<RedactedAppRegistration?> GetAppRegistrationBySlugAsync(OdinSlug appSlug, IOdinContext odinContext)
        {
            // A request that left the slug out names no app (a peer's by-slug lookup is a 404, not a 500)
            if (!appSlug.HasValue)
            {
                return null;
            }

            var record = await db.AppRegistrations.GetByAppSlugAsync(appSlug);
            return record == null ? null : FromRecord(record).Redacted();
        }

        public async Task<IOdinContext?> GetAppPermissionContextAsync(ClientAuthenticationToken token, IOdinContext odinContext)
        {
            async Task<IOdinContext?> Creator()
            {
                var (appClient, appReg) = await ValidateClientAuthTokenAsync(token);

                if (null == appClient || null == appReg)
                {
                    throw new OdinSecurityException("Invalid token");
                }

                var accessReg = appClient.ServerHalfOfClientKey;

                if (!string.IsNullOrEmpty(appReg.CorsHostName))
                {
                    //just in case something changed in the db record
                    AppUtil.AssertValidCorsHeader(appReg.CorsHostName);
                }

                var grantDictionary = new Dictionary<Guid, KeyStore>
                {
                    { ByteArrayUtil.ReduceSHA256Hash("app_exchange_grant"), appReg.AppKeyStore }
                };

                //Note: isOwner = true because we passed ValidateClientAuthToken for an ap token above 
                var permissionContext = await exchangeGrantService.CreatePermissionContext(
                    token,
                    grantDictionary,
                    accessReg,
                    odinContext,
                    includeAnonymousDrives: true);

                var dotYouContext = new OdinContext()
                {
                    Tenant = tenantContext.HostOdinId,
                    Caller = new CallerContext(
                        odinId: tenantContext.HostOdinId,
                        masterKey: null,
                        securityLevel: SecurityGroupType.Owner,
                        odinClientContext: new OdinClientContext()
                        {
                            ClientIdOrDomain = appReg.Name,
                            CorsHostName = appReg.CorsHostName,
                            AccessRegistrationId = accessReg.Id,
                            AppId = appReg.AppId,
                            DevicePushNotificationKey = null,
                            ZkDriveKeys = await GetZkDriveKeysAsync(appReg),
                            HoldsZkAppKey = appClient.ZkAppKeyId != null &&
                                            appClient.ZkAppKeyId == AppRegistration.ZkAppKeyId(appReg.ZkMasterKeyEncryptedAppKey)
                        })
                };


                dotYouContext.SetPermissionContext(permissionContext);
                return dotYouContext;
            }

            var result = await cache.GetOrAddContextAsync(token, Creator);
            return result;
        }

        /// <summary>The device and the app a token is for; both null when the token isn't valid.</summary>
        private async Task<(AppClientRegistration? appClient, AppRegistration? appReg)> ValidateClientAuthTokenAsync(
            ClientAuthenticationToken authToken)
        {
            var appClient = await clientRegistrationStorage.GetAsync<AppClientRegistration>(authToken.Id);
            if (null == appClient)
            {
                logger.LogDebug("null app client");
                return (null, null);
            }

            var appReg = await this.GetAppRegistrationInternalAsync(appClient.AppId);

            if (null == appReg || null == appReg.AppKeyStore)
            {
                logger.LogDebug("null app registration or app registration grant");
                return (null, null);
            }

            if (appClient.ServerHalfOfClientKey.IsRevoked || appReg.AppKeyStore.IsRevoked)
            {
                logger.LogDebug("app client or app is revoked");
                return (null, null);
            }

            return (appClient, appReg);
        }

        /// <summary>
        /// The app's locked keys to the zero-knowledge drives it has a copy for (a copy lives only as long as the grant
        /// that reads the drive, <see cref="SaveAsync"/>), with each drive's locked write-only private key
        /// (docs/zero-knowledge-drive-api-plan.md, step 3); null when there are none.
        /// </summary>
        private async Task<IReadOnlyDictionary<Guid, ZkAppDriveKey>?> GetZkDriveKeysAsync(AppRegistration appReg)
        {
            if (appReg.ZkAppKeyEncryptedStorageKeys is not { Count: > 0 } copies)
            {
                return null;
            }

            var keys = new Dictionary<Guid, ZkAppDriveKey>();
            foreach (var (driveId, locked) in copies)
            {
                if (await driveManager.GetDriveAsync(driveId) is { IsZeroKnowledge: true } drive)
                {
                    keys[driveId] = new ZkAppDriveKey(locked, drive.ZkStorageKeyEncryptedWriteOnlyPrivateKey);
                }
            }

            return keys.Count > 0 ? keys : null;
        }

        /// <summary>
        /// The drives whose key the app holds a copy of once they are zero knowledge: those it is granted reading, as
        /// <see cref="StorageDrive.PermissionNeedsKey"/> says.  Only writing deposits (sealed to the drive's public key).
        /// </summary>
        private static IEnumerable<Guid> KeyedDriveIds(AppRegistration appReg) =>
            appReg.AppKeyStore?.DriveGrants?
                .Where(g => StorageDrive.PermissionNeedsKey(g.PermissionedDrive.Permission))
                .Select(g => g.DriveId).Distinct() ?? [];

        /// <summary>The id of the app's zero-knowledge key, which the consent page checks the key it sealed against.</summary>
        public async Task<string?> GetZkAppKeyIdAsync(GuidId appId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            var appReg = await GetAppRegistrationInternalAsync(appId)
                         ?? throw new OdinClientException("Invalid AppId", OdinClientErrorCode.AppNotRegistered);
            return AppRegistration.ZkAppKeyId(appReg.ZkMasterKeyEncryptedAppKey);
        }

        //

        public async Task<List<ZkAppKeysResponse>> GetZkAppKeysAsync(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var result = new List<ZkAppKeysResponse>();
            foreach (var appReg in await GetAllAppRegistrationsInternalAsync())
            {
                var drives = new List<ZkAppDrive>();
                foreach (var driveId in KeyedDriveIds(appReg))
                {
                    if (await driveManager.GetDriveAsync(driveId) is { } drive)
                    {
                        drives.Add(new ZkAppDrive
                        {
                            DriveId = driveId,
                            IsZeroKnowledge = drive.IsZeroKnowledge,
                            ZkMasterKeyEncryptedStorageKey = drive.IsZeroKnowledge ? drive.ZkMasterKeyEncryptedStorageKey : null,
                            HasAppCopy = appReg.ZkAppKeyEncryptedStorageKeys?.ContainsKey(driveId) == true
                        });
                    }
                }

                result.Add(new ZkAppKeysResponse
                {
                    AppId = appReg.AppId,
                    AppSlug = appReg.AppSlug.ToString(),
                    ZkMasterKeyEncryptedAppKey = appReg.ZkMasterKeyEncryptedAppKey,
                    Drives = drives
                });
            }

            return result;
        }

        public async Task SaveZkAppKeysAsync(ZkAppKeysRequest request, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            request.Validate();

            var appReg = await GetAppRegistrationInternalAsync(request.AppId)
                         ?? throw new OdinClientException("Invalid AppId", OdinClientErrorCode.AppNotRegistered);

            foreach (var driveId in request.ZkAppKeyEncryptedStorageKeys.Keys)
            {
                if (await driveManager.GetDriveAsync(driveId) is not { IsZeroKnowledge: true })
                {
                    throw new OdinClientException($"Drive {driveId} is not zero knowledge", OdinClientErrorCode.ZkDriveNotGrantedToApp);
                }
            }

            ApplyZkAppKeys(appReg, request);
            await SaveAsync(appReg);
            await ResetAppPermissionContextCacheAsync();
        }

        /// <summary>
        /// Converting a drive that keeps its key, in the conversion's transaction: each app reading it gets its copy of
        /// the drive's key, locked with its zero-knowledge key, and every app's grant on it loses the key the server
        /// could open, keeping its permission (docs/zero-knowledge-drive-api-plan.md, 3.3).  Every app reading the drive
        /// must get a copy, so none is cut off.
        /// </summary>
        public async Task SetZkAppKeysForConversionAsync(Guid driveId, IReadOnlyCollection<ZkAppKeysRequest> requests,
            IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var byApp = requests.ToDictionary(r => r.AppId);
            foreach (var appReg in await GetAllAppRegistrationsInternalAsync())
            {
                var keyRemoved = DriveGrant.RemoveStorageKey(appReg.AppKeyStore?.DriveGrants, driveId);
                if (!KeyedDriveIds(appReg).Contains(driveId))
                {
                    if (keyRemoved)
                    {
                        await SaveAsync(appReg);
                    }

                    continue;
                }

                if (!byApp.Remove(appReg.AppId, out var request) || !request.ZkAppKeyEncryptedStorageKeys.ContainsKey(driveId))
                {
                    throw new OdinClientException($"App {appReg.AppId} is granted drive {driveId} and needs its copy of the key",
                        OdinClientErrorCode.ZkAppKeyMissing);
                }

                // Only the drive being converted: any other drive's copy goes through SaveZkAppKeysAsync, which checks
                // the drive is zero knowledge
                if (request.ZkAppKeyEncryptedStorageKeys.Count != 1)
                {
                    throw new OdinClientException($"A conversion carries only the copy of drive {driveId}", OdinClientErrorCode.ArgumentError);
                }

                ApplyZkAppKeys(appReg, request);
                await SaveAsync(appReg);
            }

            if (byApp.Count > 0)
            {
                throw new OdinClientException($"App {byApp.Keys.First()} isn't granted drive {driveId}",
                    OdinClientErrorCode.ZkDriveNotGrantedToApp);
            }

            await ResetAppPermissionContextCacheAsync();
        }

        /// <summary>
        /// Sets what the browser locked for an app: its zero-knowledge key only when it has none (replacing it would
        /// strand every copy locked with the old one), and a copy of the key of each drive the app is granted.
        /// </summary>
        private static void ApplyZkAppKeys(AppRegistration appReg, ZkAppKeysRequest request)
        {
            if (request.ZkMasterKeyEncryptedAppKey != null && request.ZkMasterKeyEncryptedAppKey != appReg.ZkMasterKeyEncryptedAppKey)
            {
                if (appReg.ZkMasterKeyEncryptedAppKey != null)
                {
                    throw new OdinClientException($"App {appReg.AppId} already has a zero-knowledge key", OdinClientErrorCode.ZkAppKeyAlreadySet);
                }

                appReg.ZkMasterKeyEncryptedAppKey = request.ZkMasterKeyEncryptedAppKey;
            }

            if (request.ZkAppKeyEncryptedStorageKeys.Count == 0)
            {
                return;
            }

            if (appReg.ZkMasterKeyEncryptedAppKey == null)
            {
                throw new OdinClientException($"App {appReg.AppId} has no zero-knowledge key to lock drive keys with",
                    OdinClientErrorCode.ZkAppKeyMissing);
            }

            var granted = KeyedDriveIds(appReg).ToHashSet();
            appReg.ZkAppKeyEncryptedStorageKeys ??= new Dictionary<Guid, string>();
            foreach (var (driveId, locked) in request.ZkAppKeyEncryptedStorageKeys)
            {
                if (!granted.Contains(driveId))
                {
                    throw new OdinClientException($"App {appReg.AppId} isn't granted drive {driveId}", OdinClientErrorCode.ZkDriveNotGrantedToApp);
                }

                appReg.ZkAppKeyEncryptedStorageKeys[driveId] = locked;
            }
        }

        /// <summary>A drive's key changed: every app's copy locks the old one, and the backfill makes new ones.</summary>
        public Task RemoveZkDriveCopiesAsync(Guid driveId, IOdinContext odinContext) =>
            UpdateAllAppsAsync(appReg => appReg.ZkAppKeyEncryptedStorageKeys?.Remove(driveId) == true, odinContext);

        public async Task<List<RedactedAppRegistration>> GetAppsGrantingCircleAsync(Guid circleId, IOdinContext odinContext)
        {
            var allApps = await GetRegisteredAppsInternalAsync();
            return allApps.Where(reg => reg.AuthorizedCircles?.Any(c => c == circleId) ?? false).ToList();
        }

        public async Task RevokeAppAsync(GuidId appId, IOdinContext odinContext)
        {
            var appReg = await this.GetAppRegistrationInternalAsync(appId);
            if (null == appReg)
            {
                // Nothing to revoke. The old blob store accepted a null here and wrote a row whose
                // payload was the literal "null"; the table cannot express that, and should not.
                return;
            }

            //TODO: do we do anything with storage DEK here?
            appReg.AppKeyStore.IsRevoked = true;

            //TODO: revoke all clients? or is the one flag enough?

            await SaveAsync(appReg);

            await ResetAppPermissionContextCacheAsync();
        }

        public async Task RemoveAppRevocationAsync(GuidId appId, IOdinContext odinContext)
        {
            var appReg = await this.GetAppRegistrationInternalAsync(appId);
            if (null == appReg)
            {
                // Nothing to revoke. The old blob store accepted a null here and wrote a row whose
                // payload was the literal "null"; the table cannot express that, and should not.
                return;
            }

            //TODO: do we do anything with storage DEK here?
            appReg.AppKeyStore.IsRevoked = false;

            await SaveAsync(appReg);

            await ResetAppPermissionContextCacheAsync();
        }

        public async Task<GuidId?> GetCallingAppIdAsync(IOdinContext odinContext)
        {
            var accessRegistrationId = odinContext.Caller.OdinClientContext?.AccessRegistrationId;
            if (accessRegistrationId == null)
            {
                return null;
            }

            var client = await clientRegistrationStorage.GetAsync<AppClientRegistration>(accessRegistrationId);
            return client?.AppId;
        }

        public async Task<List<RegisteredAppClientResponse>> GetRegisteredClientsAsync(GuidId appId, IOdinContext odinContext)
        {
            var list = await clientRegistrationStorage.GetByTypeAndCategoryIdAsync<AppClientRegistration>(AppClientRegistration.CatType,
                appId);
            var zkAppKeyId = AppRegistration.ZkAppKeyId((await GetAppRegistrationInternalAsync(appId))?.ZkMasterKeyEncryptedAppKey);
            var resp = list.Where(appClient => appClient.AppId == appId).Select(appClient => new RegisteredAppClientResponse()
            {
                HoldsZkAppKey = zkAppKeyId != null && appClient.ZkAppKeyId == zkAppKeyId,
                AppId = appClient.AppId,
                AccessRegistrationId = appClient.ServerHalfOfClientKey.Id,
                FriendlyName = appClient.FriendlyName,
                IsRevoked = appClient.ServerHalfOfClientKey.IsRevoked,
                Created = appClient.ServerHalfOfClientKey.Created,
                AccessRegistrationClientType = appClient.ServerHalfOfClientKey.AccessRegistrationClientType
            }).ToList();

            return resp;
        }

        public async Task RevokeClientAsync(GuidId accessRegistrationId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            var client = await clientRegistrationStorage.GetAsync<AppClientRegistration>(accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            client.ServerHalfOfClientKey.IsRevoked = true;
            await SaveClientAsync(client);
        }

        /// <summary>
        /// Deletes the current client calling into the system.  This is used to 'logout' an app
        /// </summary>
        public async Task DeleteCurrentAppClientAsync(IOdinContext odinContext)
        {
            var context = odinContext;
            var accessRegistrationId = context.Caller.OdinClientContext?.AccessRegistrationId;

            var validAccess = accessRegistrationId != null &&
                              context.Caller.SecurityLevel == SecurityGroupType.Owner;

            if (!validAccess)
            {
                throw new OdinSecurityException("Invalid call to Delete app client");
            }

            var client = await clientRegistrationStorage.GetAsync<AppClientRegistration>(accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            await clientRegistrationStorage.DeleteAsync(accessRegistrationId);
        }

        public async Task DeleteClientAsync(GuidId accessRegistrationId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var client = await clientRegistrationStorage.GetAsync<AppClientRegistration>(accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            await clientRegistrationStorage.DeleteAsync(accessRegistrationId);
        }

        public async Task AllowClientAsync(GuidId accessRegistrationId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var client = await clientRegistrationStorage.GetAsync<AppClientRegistration>(accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            client.ServerHalfOfClientKey.IsRevoked = false;
            await SaveClientAsync(client);
        }

        /// <summary>
        /// Deletes the registration row only -- the last step of <see cref="AppUninstallService.UninstallAsync"/>,
        /// which removes the app's clients, grants, circles and drives first. Off the interface so nothing else can
        /// half-delete an app.
        /// </summary>
        internal async Task DeleteRegistrationAsync(GuidId appId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var app = await GetAppRegistrationInternalAsync(appId);

            if (null == app)
            {
                throw new OdinClientException("Invalid App Id", OdinClientErrorCode.AppNotRegistered);
            }

            await db.AppRegistrations.DeleteAsync(appId);
            await ResetAppPermissionContextCacheAsync();
        }

        public Task RemoveCirclesFromAllAppsAsync(IReadOnlyCollection<Guid> circleIds, IOdinContext odinContext) =>
            UpdateAllAppsAsync(appReg => appReg.AuthorizedCircles?.RemoveAll(circleIds.Contains) > 0, odinContext);

        public Task RemoveDriveFromAllAppsAsync(Guid driveId, IOdinContext odinContext) =>
            UpdateAllAppsAsync(appReg =>
            {
                var removed = appReg.AppKeyStore?.DriveGrants?.RemoveAll(g => g.DriveId == driveId) > 0;

                var circleDrives = appReg.CircleMemberPermissionGrant?.Drives?.ToList();
                if (circleDrives?.RemoveAll(g => g.PermissionedDrive.Drive.Alias == driveId) > 0)
                {
                    appReg.CircleMemberPermissionGrant!.Drives = circleDrives;
                    removed = true;
                }

                return removed;
            }, odinContext);

        /// <summary>Applies <paramref name="update"/> to every registration, saving each one it changed.</summary>
        private async Task UpdateAllAppsAsync(Func<AppRegistration, bool> update, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            foreach (var appReg in await GetAllAppRegistrationsInternalAsync())
            {
                if (update(appReg))
                {
                    await SaveAsync(appReg);
                }
            }

            await ResetAppPermissionContextCacheAsync();
        }

        public async Task<List<RedactedAppRegistration>> GetRegisteredAppsAsync(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            return await GetRegisteredAppsInternalAsync();
        }

        private async Task<List<RedactedAppRegistration>> GetRegisteredAppsInternalAsync()
        {
            return (await GetAllAppRegistrationsInternalAsync()).Select(app => app.Redacted()).ToList();
        }

        private async Task<List<AppRegistration>> GetAllAppRegistrationsInternalAsync()
        {
            var apps = (await db.AppRegistrations.GetAllAsync()).Select(FromRecord).ToList();

            // Pre-v13 the registrations are still blob rows -- see LegacyDefinitionStore.  A union
            // rather than a replacement: an app registered during the window is already in the table,
            // and it is the newer of the two.  The move skips ids the table already holds, so the pair
            // resolves the same way afterwards.
            if (await legacyStore.IsPreMoveAsync())
            {
                var known = apps.Select(a => (Guid)a.AppId).ToHashSet();
                apps.AddRange((await legacyStore.ReadAppRegistrationsAsync())
                    .Where(a => !known.Contains((Guid)a.AppId)));
            }

            return apps;
        }

        private async Task SaveClientAsync(AppClientRegistration appClientRegistration)
        {
            await clientRegistrationStorage.SaveAsync(appClientRegistration);
        }

        private async Task<AppRegistration?> GetAppRegistrationInternalAsync(GuidId appId)
        {
            var record = await db.AppRegistrations.GetAsync(appId);
            if (record != null)
            {
                return FromRecord(record);
            }

            // Pre-v13 only.  After the move a missing row means the app was deleted, and the blob copy
            // it left behind must not answer for it -- LegacyDefinitionStore says why the gate is the
            // version and not the miss.
            if (!await legacyStore.IsPreMoveAsync())
            {
                return null;
            }

            return (await legacyStore.ReadAppRegistrationsAsync())
                .SingleOrDefault(a => (Guid)a.AppId == (Guid)appId);
        }

        private async Task NotifyAppChanged(AppRegistration? oldAppRegistration, AppRegistration newAppRegistration,
            IOdinContext odinContext)
        {
            await mediator.Publish(new AppRegistrationChangedNotification
            {
                OldAppRegistration = oldAppRegistration,
                NewAppRegistration = newAppRegistration,
                OdinContext = odinContext,
            });
        }

        /// <summary>
        /// The slug the app will hold: the one it asked for.
        /// </summary>
        /// <remarks>
        /// Required.  A slug is the app half of <c>/apps/{appSlug}/drives/{driveSlug}</c>, so an app
        /// without one is unaddressable; deriving one from the display name produced addresses nobody
        /// chose ("Homebase - Location" became <c>homebase-locat</c>), which is why a missing slug is now
        /// refused instead.  No exceptions: a built-in sends the slug its tree definition names.
        /// <para>
        /// A requested slug is taken as given (lowercased, as every <see cref="OdinSlug"/> is) or refused
        /// -- never quietly replaced with a derived one.
        /// It is an address other identities resolve against, so handing back a different one would be
        /// worse than saying no.  Registration is first-come, and
        /// <c>UNIQUE(identityId, AppSlug)</c> would refuse it at the database anyway; this only makes the
        /// refusal a clear client error rather than a constraint violation.
        /// </para>
        /// </remarks>
        private async Task<OdinSlug> AssignSlugAsync(Guid appId, OdinSlug requested)
        {
            // Required for every app, built-ins included (#1901): missing or malformed is a client error.
            // A built-in's slug is defined in the tree (WellknownAppDefinition.AppSlug) and
            // BuiltinProvisioner sends it like any other caller.
            var requestedSlug = OdinSlug.Required(requested, nameof(AppRegistrationRequest.AppSlug));

            // The tree's slugs belong to the apps it names, registered or not: "chat" resolving to
            // whoever asked for it first would put another app at the address every client already
            // builds.
            var reservedBy = BuiltinApps.All.FirstOrDefault(
                a => a.AppId != appId && a.AppSlug == requestedSlug);
            if (reservedBy != null)
            {
                throw new OdinClientException(
                    $"The app slug '{requestedSlug}' is reserved for {reservedBy.Name} ({reservedBy.AppId}).",
                    OdinClientErrorCode.IdAlreadyExists);
            }

            // One indexed lookup: UNIQUE(identityId, AppSlug) is what this refuses ahead of, and
            // GetByAppSlugAsync is served by that same index.
            var holder = await db.AppRegistrations.GetByAppSlugAsync(requestedSlug);
            var alreadyTaken = holder != null && holder.AppId != appId;

            // Pre-v13 the table holds only what was registered during the window, so it is not the whole
            // picture: a slug free here could still be one the move is about to coin for a blob app, and
            // the move would then fail on UNIQUE(identityId, AppSlug). The legacy slugs are resolved by
            // the same generator the move uses, so refusing them here is refusing exactly what it
            // will want.
            if (!alreadyTaken && await legacyStore.IsPreMoveAsync())
            {
                alreadyTaken = (await legacyStore.ReadAppRegistrationsAsync()).Any(reg =>
                    (Guid)reg.AppId != (Guid)appId &&
                    requestedSlug == reg.AppSlug);
            }

            if (alreadyTaken)
            {
                throw new OdinClientException(
                    $"The app slug '{requestedSlug}' is already registered on this identity.",
                    OdinClientErrorCode.IdAlreadyExists);
            }

            return requestedSlug;
        }

        /// <summary>
        /// Writes a registration to its row.  AppId, AppSlug, Name and CorsHostName are columns; the rest
        /// of the registration rides <c>grantJson</c>.
        /// </summary>
        /// <summary>
        /// Gives an app the slug the tree names, if it has a different one.  Migration only.
        /// </summary>
        /// <remarks>
        /// A slug is immutable through every normal path -- an update carries the stored value forward,
        /// precisely because other identities resolve against it.  This exists because the tree is the
        /// source of truth for the apps it declares, and a registration created before that derived its
        /// slug from the display name instead: "Homebase - Location" became <c>homebase-locat</c>.
        /// </remarks>
        public async Task<bool> ApplyTreeSlugAsync(Guid appId, OdinSlug appSlug, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var record = await db.AppRegistrations.GetAsync(appId);
            if (record == null || record.AppSlug == appSlug)
            {
                return false;
            }

            var taken = (await db.AppRegistrations.GetAllAsync())
                .Any(r => r.AppId != appId && r.AppSlug == appSlug);

            if (taken)
            {
                throw new OdinSystemException(
                    $"Cannot give app {appId} the slug '{appSlug}': another app already holds it");
            }

            record.AppSlug = appSlug;
            await db.AppRegistrations.UpsertAsync(record);
            return true;
        }

        /// <summary>Saves a change to a registered app; a new one is inserted by <see cref="RegisterAppAsync"/>.</summary>
        private async Task SaveAsync(AppRegistration appReg)
        {
            await AssertKeepsKeysAsync(appReg);

            // A drive-key copy lives as long as the grant that reads with it: a grant removed or narrowed to writing,
            // or a drive deleted, takes it, so a grant given again gets a fresh one from the backfill
            if (appReg.ZkAppKeyEncryptedStorageKeys is { Count: > 0 } copies)
            {
                var keyed = KeyedDriveIds(appReg).ToHashSet();
                foreach (var driveId in copies.Keys.Where(d => !keyed.Contains(d)).ToList())
                {
                    copies.Remove(driveId);
                }
            }

            // An update, so an app uninstalled since it was read isn't written back.  Before v13 an app may live only
            // in the legacy store, and its first change moves it into the table.
            var record = ToRecord(appReg);
            if (await db.AppRegistrations.UpdateAsync(record) == 0 &&
                !(await legacyStore.IsPreMoveAsync() && await db.AppRegistrations.TryInsertAsync(record)))
            {
                throw new OdinSystemException($"App {appReg.AppId} was uninstalled while it was being changed");
            }
        }

        /// <summary>
        /// Under every change to an app (#2039): its keys are never replaced.  Its devices hold its key store key, and its
        /// zero-knowledge key locks their copies of drive keys, so a new one would cut them off.  Starting over with new
        /// keys is uninstalling the app and installing it again.
        /// </summary>
        private async Task AssertKeepsKeysAsync(AppRegistration appReg)
        {
            var stored = await GetAppRegistrationInternalAsync(appReg.AppId)
                         ?? throw new OdinSystemException($"App {appReg.AppId} isn't registered; a change never registers it");

            if (!SymmetricKeyEncryptedAes.AreSame(stored.AppKeyStore?.MasterKeyEncryptedKeyStoreKey,
                    appReg.AppKeyStore?.MasterKeyEncryptedKeyStoreKey))
            {
                throw new OdinSystemException($"A change to app {appReg.AppId} would replace its key store key");
            }

            if (stored.ZkMasterKeyEncryptedAppKey != null && stored.ZkMasterKeyEncryptedAppKey != appReg.ZkMasterKeyEncryptedAppKey)
            {
                throw new OdinSystemException($"A change to app {appReg.AppId} would replace its zero-knowledge key");
            }
        }

        private static OdinClientException AlreadyRegistered(Guid appId) =>
            new($"App {appId} is already registered", OdinClientErrorCode.AppAlreadyRegistered);

        internal static AppRegistrationsRecord ToRecord(AppRegistration appReg)
        {
            if (!appReg.AppSlug.HasValue)
            {
                throw new OdinSystemException(
                    $"App {appReg.AppId} has no valid slug ('{appReg.AppSlug}'); it cannot be persisted");
            }

            return new AppRegistrationsRecord
            {
                AppId = appReg.AppId,
                AppSlug = appReg.AppSlug,
                Name = appReg.Name,
                CorsHostName = appReg.CorsHostName,
                grantJson = OdinSystemSerializer.Serialize(appReg),
                detailsJson = null
            };
        }

        internal static AppRegistration FromRecord(AppRegistrationsRecord record)
        {
            var appReg = OdinSystemSerializer.Deserialize<AppRegistration>(record.grantJson)
                         ?? throw new OdinSystemException($"App registration {record.AppId} has unreadable grantJson");

            appReg.AppId = record.AppId;
            appReg.AppSlug = new OdinSlug(record.AppSlug);
            appReg.Name = record.Name;
            appReg.CorsHostName = record.CorsHostName;

            return appReg;
        }

        private async Task ResetAppPermissionContextCacheAsync()
        {
            await cache.ResetAsync();
        }

        /// <summary>
        /// The drives an app is granted and the ICR key it holds: an app that may use transit gets the ICR key and
        /// read-write on the transient temp drive, which it sends files directly over transit with.
        /// </summary>
        private async Task<(List<DriveGrantRequest> Drives, SensitiveByteArray? IcrKey)> GrantInputsAsync(PermissionSet? permissionSet,
            IEnumerable<DriveGrantRequest>? requested, IOdinContext odinContext)
        {
            var drives = new List<DriveGrantRequest>(requested ?? []);
            if (!HasRequestedTransit(permissionSet))
            {
                return (drives, null);
            }

            if (drives.All(d => d.PermissionedDrive.Drive != SystemDriveConstants.TransientTempDrive))
            {
                drives.Add(new DriveGrantRequest
                {
                    PermissionedDrive = new()
                    {
                        Drive = SystemDriveConstants.TransientTempDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                });
            }

            return (drives, await icrKeyService.GetDecryptedIcrKeyAsync(odinContext));
        }

        private bool HasRequestedTransit(PermissionSet? permissionSet)
        {
            if (null == permissionSet)
            {
                return false;
            }

            return permissionSet.HasKey(PermissionKeys.UseTransitRead) ||
                   permissionSet.HasKey(PermissionKeys.UseTransitWrite);
        }
    }
}