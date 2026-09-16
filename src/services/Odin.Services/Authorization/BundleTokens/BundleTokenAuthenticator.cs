#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Services.Authorization.BundleTokens;

/// <summary>
/// Turns a bundle token into a caller and a permission context: one permission group per member app,
/// each unlocked by that app's own key-store key, so the token reaches the union of the apps' grants.
/// </summary>
/// <remarks>
/// One context is cached per token.  The acting app -- the <c>AppId</c> ownership checks compare against --
/// is applied per request on a fresh <see cref="CallerContext"/>, so the cached instance is never mutated
/// and switching apps does not rebuild anything.
/// </remarks>
public class BundleTokenAuthenticator(
    IdentityDatabase db,
    IDriveManager driveManager,
    OdinContextCache contextCache,
    TenantContext tenantContext)
{
    public const string ActingAppHeader = "X-ODIN-APP-ID";

    private const string AppGroupPrefix = "app:";
    private static readonly TimeSpan MaxCacheDuration = TimeSpan.FromMinutes(60);

    /// <summary>
    /// The authenticated context for <paramref name="token"/>, acting as <paramref name="actingAppId"/>
    /// (the primary app when null or empty).  Null when the token is unknown, revoked, expired, its primary
    /// app is gone or revoked, or the acting app is not one the token can reach.
    /// </summary>
    public async Task<IOdinContext?> AuthenticateAsync(ClientAuthenticationToken token, string? actingAppId,
        IOdinContext odinContext)
    {
        if (token.ClientTokenType != ClientTokenType.AppBundle)
        {
            return null;
        }

        var record = await db.BundleTokens.GetAsync(token.Id);
        if (record == null)
        {
            return null;
        }

        var remaining = TimeSpan.FromMilliseconds(record.expiresAt.milliseconds - UnixTimeUtc.Now().milliseconds);
        if (remaining < TimeSpan.FromSeconds(1))
        {
            return null;
        }

        // Expiry is honoured inside the cache window too.
        var cached = await contextCache.GetOrAddContextAsync(token,
            () => BuildAsync(token, odinContext),
            remaining < MaxCacheDuration ? remaining : MaxCacheDuration);

        if (cached == null)
        {
            return null;
        }

        var primary = cached.Caller.OdinClientContext.AppId.Value;
        Guid acting;
        if (string.IsNullOrWhiteSpace(actingAppId))
        {
            acting = primary;
        }
        else if (!Guid.TryParse(actingAppId, out acting))
        {
            return null;
        }

        if (!cached.PermissionsContext.PermissionGroups.ContainsKey(GroupKey(acting)))
        {
            return null;
        }

        var result = new OdinContext
        {
            Tenant = cached.Tenant,
            AuthTokenCreated = cached.AuthTokenCreated,
            Caller = new CallerContext(
                odinId: cached.Caller.OdinId,
                masterKey: null,
                securityLevel: SecurityGroupType.Owner,
                odinClientContext: new OdinClientContext
                {
                    ClientIdOrDomain = cached.Caller.OdinClientContext.ClientIdOrDomain,
                    CorsHostName = cached.Caller.OdinClientContext.CorsHostName,
                    AccessRegistrationId = cached.Caller.OdinClientContext.AccessRegistrationId,
                    AppId = acting,
                    DevicePushNotificationKey = null
                })
        };

        result.SetPermissionContext(cached.PermissionsContext);
        return result;
    }

    private async Task<IOdinContext?> BuildAsync(ClientAuthenticationToken token, IOdinContext odinContext)
    {
        var record = await db.BundleTokens.GetAsync(token.Id);
        if (record == null)
        {
            return null;
        }

        var accessRegistration = OdinSystemSerializer.Deserialize<ServerHalfOfClientKey>(record.accessRegistrationJson);
        if (accessRegistration == null || accessRegistration.IsRevoked)
        {
            return null;
        }

        SensitiveByteArray bundleKey;
        SensitiveByteArray sharedSecret;
        try
        {
            (bundleKey, sharedSecret) = accessRegistration.DecryptUsingClientAuthenticationToken(token);
        }
        catch
        {
            // Wrong client half.
            return null;
        }

        var members = await db.BundleTokenApps.GetByTokenIdAsync(token.Id);
        var registrations = (await db.AppRegistrations.GetAllAsync())
            .Select(AppRegistrationService.FromRecord)
            .ToDictionary(r => r.AppId.Value);

        if (!registrations.TryGetValue(record.primaryAppId, out var primary) || primary.AppKeyStore.IsRevoked)
        {
            return null;
        }

        var groups = new Dictionary<string, PermissionGroup>();
        SensitiveByteArray? primaryKeyStoreKey = null;

        try
        {
            foreach (var member in members)
            {
                // A revoked or deleted member drops out; the rest of the token keeps working.
                if (!registrations.TryGetValue(member.appId, out var registration) || registration.AppKeyStore.IsRevoked)
                {
                    continue;
                }

                var encrypted = OdinSystemSerializer.DeserializeOrThrow<SymmetricKeyEncryptedAes>(member.encryptedKeyStoreKeyJson);
                var keyStoreKey = encrypted.DecryptKeyClone(bundleKey);

                groups[GroupKey(member.appId)] = new PermissionGroup(
                    registration.AppKeyStore.PermissionSet,
                    registration.AppKeyStore.DriveGrants,
                    keyStoreKey,
                    registration.AppKeyStore.KeyStoreKeyEncryptedIcrKey);

                if (member.appId == record.primaryAppId)
                {
                    primaryKeyStoreKey = keyStoreKey;
                }
            }
        }
        finally
        {
            bundleKey.Wipe();
        }

        if (!groups.ContainsKey(GroupKey(record.primaryAppId)))
        {
            return null;
        }

        var anonymousDrives = await driveManager.GetAnonymousDrivesAsync(PageOptions.All, odinContext);
        groups["anonymous_drives"] = new PermissionGroup(new PermissionSet(),
            anonymousDrives.Results.Select(d => new DriveGrant
            {
                DriveId = d.Id,
                PermissionedDrive = new PermissionedDrive { Drive = d.TargetDriveInfo, Permission = DrivePermission.Read }
            }).ToList(),
            null, null);

        var grantedKeys = members
            .Where(m => groups.ContainsKey(GroupKey(m.appId)))
            .SelectMany(m => registrations[m.appId].AppKeyStore.PermissionSet?.Keys ?? []);
        var impliedKeys = PermissionKeyImplications.ResolveImpliedKeys(grantedKeys);
        if (impliedKeys.Count > 0)
        {
            groups["implied_permissions"] = new PermissionGroup(new PermissionSet(impliedKeys), null, null, null);
        }

        var context = new OdinContext
        {
            Tenant = tenantContext.HostOdinId,
            AuthTokenCreated = accessRegistration.Created,
            Caller = new CallerContext(
                odinId: tenantContext.HostOdinId,
                masterKey: null,
                securityLevel: SecurityGroupType.Owner,
                odinClientContext: new OdinClientContext
                {
                    ClientIdOrDomain = record.friendlyName,
                    CorsHostName = primary.CorsHostName,
                    AccessRegistrationId = record.tokenId,
                    AppId = record.primaryAppId,
                    DevicePushNotificationKey = null
                })
        };

        context.SetPermissionContext(new PermissionContext(groups, sharedSecret, keyStoreKey: primaryKeyStoreKey));
        return context;
    }

    private static string GroupKey(Guid appId) => $"{AppGroupPrefix}{appId:N}";
}
