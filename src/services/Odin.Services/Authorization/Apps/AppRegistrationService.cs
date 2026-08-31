#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version12tov13;
using Odin.Services.Drives;
using Odin.Services.Mediator;
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
        OdinContextCache cache)
        : IAppRegistrationService
    {

        public async Task<RedactedAppRegistration> RegisterAppAsync(AppRegistrationRequest request, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            if (!string.IsNullOrEmpty(request.CorsHostName))
            {
                AppUtil.AssertValidCorsHeader(request.CorsHostName);
            }

            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var hasTransit = this.HasRequestedTransit(request.PermissionSet);
            var icrKey = hasTransit ? await icrKeyService.GetDecryptedIcrKeyAsync(odinContext) : null;

            var drives = new List<DriveGrantRequest>(request.Drives ?? new List<DriveGrantRequest>());

            if (hasTransit)
            {
                // ensure the transient temp drive is added, once
                // Apps must be able to access the transient drive to send files directly over transit
                if (drives.All(d => d.PermissionedDrive.Drive != SystemDriveConstants.TransientTempDrive))
                {
                    drives.Add(new DriveGrantRequest()
                    {
                        PermissionedDrive = new()
                        {
                            Drive = SystemDriveConstants.TransientTempDrive,
                            Permission = DrivePermission.ReadWrite
                        }
                    });
                }
            }

            var appGrant = await exchangeGrantService.CreateExchangeGrantAsync(
                keyStoreKey, request.PermissionSet!, drives, new MasterKeyStorageKeySource(masterKey), masterKey, icrKey);

            //TODO: add check to ensure app name is unique
            //TODO: add check if app is already registered

            var appReg = new AppRegistration()
            {
                AppId = request.AppId,
                AppSlug = await AssignSlugAsync(request.AppId, request.Name, request.AppSlug),
                Name = request.Name,
                AppKeyStore = appGrant,

                CorsHostName = request.CorsHostName,
                CircleMemberPermissionGrant = request.CircleMemberPermissionGrant,
                AuthorizedCircles = request.AuthorizedCircles
            };

            await SaveAsync(appReg);

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

            //TODO: Should we regen the key store key?  

            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = appReg.AppKeyStore.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            var hasTransit = this.HasRequestedTransit(request.PermissionSet);
            var icrKey = hasTransit ? await icrKeyService.GetDecryptedIcrKeyAsync(odinContext) : null;

            var drives = new List<DriveGrantRequest>(request.Drives ?? new List<DriveGrantRequest>());

            if (hasTransit)
            {
                // ensure the transient temp drive is added, once
                // Apps must be able to access the transient drive to send files directly over transit
                if (drives.All(d => d.PermissionedDrive.Drive != SystemDriveConstants.TransientTempDrive))
                {
                    drives.Add(new DriveGrantRequest()
                    {
                        PermissionedDrive = new()
                        {
                            Drive = SystemDriveConstants.TransientTempDrive,
                            Permission = DrivePermission.ReadWrite
                        }
                    });
                }
            }

            appReg.AppKeyStore = await exchangeGrantService.CreateExchangeGrantAsync(keyStoreKey, request.PermissionSet!, drives,
                new MasterKeyStorageKeySource(masterKey), masterKey, icrKey);

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

            if (request.AppId == SystemAppConstants.ChatAppId)
            {
                foreach (var cid in SystemAppConstants.ChatAppRegistrationRequest.AuthorizedCircles)
                {
                    request.AuthorizedCircles.EnsureItem(cid);
                }
            }

            if (request.AppId == SystemAppConstants.MailAppId)
            {
                foreach (var cid in SystemAppConstants.MailAppRegistrationRequest.AuthorizedCircles)
                {
                    request.AuthorizedCircles.EnsureItem(cid);
                }
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

                CircleMemberPermissionGrant = request.CircleMemberPermissionGrant,
                AuthorizedCircles = request.AuthorizedCircles
            };

            await SaveAsync(updatedAppReg);

            //TODO: consider optimize by checking if anything actually changed before calling notify app changed

            await NotifyAppChanged(oldRegistration, updatedAppReg, odinContext);
            await ResetAppPermissionContextCacheAsync();
        }

        public async Task<(ClientAccessToken cat, string corsHostName)> RegisterClientAsync(GuidId appId, string friendlyName,
            IOdinContext odinContext)
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

            var appClient = new AppClientRegistration(odinContext.Tenant, appId, friendlyName, accessRegistration);
            await SaveClientAsync(appClient);
            return (cat, appReg.CorsHostName);
        }

        public async Task<RedactedAppRegistration?> GetAppRegistration(GuidId appId, IOdinContext odinContext)
        {
            var result = await GetAppRegistrationInternalAsync(appId);
            return result?.Redacted();
        }

        public async Task<IOdinContext?> GetAppPermissionContextAsync(ClientAuthenticationToken token, IOdinContext odinContext)
        {
            async Task<IOdinContext?> Creator()
            {
                var (isValid, accessReg, appReg) = await ValidateClientAuthTokenAsync(token, odinContext);

                if (!isValid || null == appReg || accessReg == null)
                {
                    throw new OdinSecurityException("Invalid token");
                }

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
                            DevicePushNotificationKey = null
                        })
                };


                dotYouContext.SetPermissionContext(permissionContext);
                return dotYouContext;
            }

            var result = await cache.GetOrAddContextAsync(token, Creator);
            return result;
        }

        public async Task<(bool isValid, ServerHalfOfClientKey? accessReg, AppRegistration? appRegistration)> ValidateClientAuthTokenAsync(
            ClientAuthenticationToken authToken, IOdinContext odinContext)
        {
            var appClient = await clientRegistrationStorage.GetAsync<AppClientRegistration>(authToken.Id);
            if (null == appClient)
            {
                logger.LogDebug("null app client");
                return (false, null, null);
            }

            var appReg = await this.GetAppRegistrationInternalAsync(appClient.AppId);

            if (null == appReg || null == appReg.AppKeyStore)
            {
                logger.LogDebug("null app registration or app registration grant");
                return (false, null, null);
            }

            if (appClient.ServerHalfOfClientKey.IsRevoked || appReg.AppKeyStore.IsRevoked)
            {
                logger.LogDebug("app client or app is revoked");
                return (false, null, null);
            }

            return (true, appClient.ServerHalfOfClientKey, appReg);
        }

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
            var resp = list.Where(appClient => appClient.AppId == appId).Select(appClient => new RegisteredAppClientResponse()
            {
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

        public async Task DeleteAppAsync(GuidId appId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var app = await GetAppRegistrationInternalAsync(appId);

            if (null == app)
            {
                throw new OdinClientException("Invalid App Id", OdinClientErrorCode.AppNotRegistered);
            }

            await db.AppRegistrations.DeleteAsync(appId);

            //TODO: reenable this after youauth domain work

            //
            // var clientsByApp = _appClientValueStorage.GetByKey2<AppClient>(appId);
            // using (_TenantSystemStorage.CreateCommitUnitOfWork())
            // {
            //     foreach (var c in clientsByApp)
            //     {
            //         _appClientValueStorage.Delete(c.AccessRegistration.Id);
            //     }
            //
            //     _appRegistrationValueStorage.Delete(appId);
            // }
        }

        public async Task<List<RedactedAppRegistration>> GetRegisteredAppsAsync(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            return await GetRegisteredAppsInternalAsync();
        }

        private async Task<List<RedactedAppRegistration>> GetRegisteredAppsInternalAsync()
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

            var redactedList = apps.Select(app => app.Redacted()).ToList();
            return redactedList;
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
        /// Empties the cache and creates a new instance that can be built
        /// </summary>
        /// <summary>
        /// Picks a slug for a newly registered app, unique against those already registered.
        /// </summary>
        /// <summary>
        /// The slug the app will hold: the one it asked for, or one derived from its name.
        /// </summary>
        /// <remarks>
        /// Not required yet.  An app that omits it gets a derived slug, which is what every registration
        /// that predates the field got, so nothing that works today starts failing.
        /// <para>
        /// A requested slug is taken verbatim or refused -- never quietly replaced with a derived one.
        /// It is an address other identities resolve against, so handing back a different one would be
        /// worse than saying no.  Registration is first-come (<c>docs/drive-addressing.md</c>), and
        /// <c>UNIQUE(identityId, AppSlug)</c> would refuse it at the database anyway; this only makes the
        /// refusal a clear client error rather than a constraint violation.
        /// </para>
        /// </remarks>
        private async Task<string> AssignSlugAsync(Guid appId, string name, string requestedSlug)
        {
            var existing = await db.AppRegistrations.GetAllAsync();

            // Seed with the slugs actually stored, not re-derived ones -- an app holding "acme-2" still
            // holds it whatever its name slugifies to today.
            var taken = new HashSet<string>(
                existing.Where(r => r.AppId != appId).Select(r => r.AppSlug),
                StringComparer.Ordinal);

            // Pre-v13 the table holds only what was registered during the window, so it is not the whole
            // picture: a slug free here could still be one the move is about to coin for a blob app, and
            // the move would then fail on UNIQUE(identityId, AppSlug). The legacy slugs are resolved by
            // the same generator the move uses, so reserving them here is reserving exactly what it
            // will want.
            if (await legacyStore.IsPreMoveAsync())
            {
                foreach (var reg in await legacyStore.ReadAppRegistrationsAsync())
                {
                    if ((Guid)reg.AppId != (Guid)appId)
                    {
                        taken.Add(reg.AppSlug);
                    }
                }
            }

            // A whitespace-only value means "not set", the same as null or empty. Clients serialize an
            // unset field as "" or " " routinely, and without this the three spellings diverge: null and
            // "" derive a slug while "   " fails validation and throws. Not coercion of a real slug --
            // there is no address inside "   " to preserve. Anything with actual content is still
            // validated and rejected on failure, so " chat " is an error, never trimmed to "chat".
            if (string.IsNullOrWhiteSpace(requestedSlug))
            {
                return AppSlugGenerator.Generate(appId, name, taken);
            }

            OdinSlug.AssertValidOrNull(requestedSlug, nameof(AppRegistrationRequest.AppSlug));

            if (taken.Contains(requestedSlug))
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
        public async Task<bool> ApplyTreeSlugAsync(Guid appId, string appSlug, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            if (!OdinSlug.IsValid(appSlug))
            {
                throw new OdinSystemException($"'{appSlug}' is not a valid app slug");
            }

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

        private async Task SaveAsync(AppRegistration appReg)
        {
            await db.AppRegistrations.UpsertAsync(ToRecord(appReg));
        }

        internal static AppRegistrationsRecord ToRecord(AppRegistration appReg)
        {
            if (!AppSlugGenerator.IsValid(appReg.AppSlug))
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
            appReg.AppSlug = record.AppSlug;
            appReg.Name = record.Name;
            appReg.CorsHostName = record.CorsHostName;

            return appReg;
        }

        private async Task ResetAppPermissionContextCacheAsync()
        {
            await cache.ResetAsync();
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