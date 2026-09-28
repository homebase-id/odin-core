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
/// A context is cached per (token, acting app) and built with that acting app's <c>AppId</c>, so the
/// request path is a cache lookup and an expiry comparison -- no database read, no per-request copy.
/// Revoking, deleting or changing a token resets the cache (see <see cref="BundleTokenService"/>).
/// </remarks>
public class BundleTokenAuthenticator(
    IdentityDatabase db,
    IDriveManager driveManager,
    OdinContextCache contextCache,
    TenantContext tenantContext)
{
    public const string ActingAppHeader = "X-ODIN-APP-ID";

    /// <summary>
    /// The authenticated context for <paramref name="token"/>, acting as <paramref name="actingAppId"/>
    /// (the primary app when null or empty).  Null when the token is unknown, revoked, expired, its primary
    /// app is gone or revoked, or the acting app is not one the token can reach.
    /// </summary>
    public async Task<IOdinContext?> AuthenticateAsync(ClientAuthenticationToken token, string? actingAppId,
        IOdinContext odinContext)
    {
        Guid? acting = null;
        if (!string.IsNullOrWhiteSpace(actingAppId))
        {
            if (!Guid.TryParse(actingAppId, out var parsed))
            {
                return null;
            }

            acting = parsed;
        }

        var context = await contextCache.GetOrAddContextAsync(token,
            () => BuildAsync(token, acting, odinContext),
            keySuffix: acting?.ToString("N"));

        return context is BundleOdinContext bundle && bundle.ExpiresAt.milliseconds > UnixTimeUtc.Now().milliseconds
            ? bundle
            : null;
    }

    private async Task<IOdinContext?> BuildAsync(ClientAuthenticationToken token, Guid? actingAppId, IOdinContext odinContext)
    {
        var record = await db.BundleTokens.GetAsync(token.Id);
        if (record == null || record.expiresAt.milliseconds <= UnixTimeUtc.Now().milliseconds)
        {
            return null;
        }

        var acting = actingAppId ?? record.primaryAppId;

        var accessRegistration = OdinSystemSerializer.Deserialize<ServerHalfOfClientKey>(record.accessRegistrationJson);
        if (accessRegistration == null || accessRegistration.IsRevoked)
        {
            return null;
        }

        // The primary app first: when it is gone or revoked the token fails, and nothing else is worth loading.
        var primary = await LoadActiveAppAsync(record.primaryAppId);
        if (primary == null)
        {
            return null;
        }

        var members = await db.BundleTokenApps.GetByTokenIdAsync(token.Id);
        if (members.All(m => m.appId != acting))
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

        var groups = new Dictionary<string, PermissionGroup>();
        var grantedKeys = new List<int>();
        var memberIds = new HashSet<Guid>();
        SensitiveByteArray? primaryKeyStoreKey = null;

        try
        {
            foreach (var member in members)
            {
                // A revoked or deleted member drops out; the rest of the token keeps working.
                var app = member.appId == primary.AppId.Value ? primary : await LoadActiveAppAsync(member.appId);
                if (app == null)
                {
                    continue;
                }

                var keyStoreKey = OdinSystemSerializer.DeserializeOrThrow<SymmetricKeyEncryptedAes>(member.encryptedKeyStoreKeyJson)
                    .DecryptKeyClone(bundleKey);

                groups[$"app:{member.appId:N}"] = new PermissionGroup(
                    app.AppKeyStore.PermissionSet,
                    app.AppKeyStore.DriveGrants,
                    keyStoreKey,
                    app.AppKeyStore.KeyStoreKeyEncryptedIcrKey);

                grantedKeys.AddRange(app.AppKeyStore.PermissionSet?.Keys ?? []);
                memberIds.Add(member.appId);
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

        if (!memberIds.Contains(acting))
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

        var impliedKeys = PermissionKeyImplications.ResolveImpliedKeys(grantedKeys);
        if (impliedKeys.Count > 0)
        {
            groups["implied_permissions"] = new PermissionGroup(new PermissionSet(impliedKeys), null, null, null);
        }

        var context = new BundleOdinContext
        {
            ExpiresAt = record.expiresAt,
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
                    AppId = acting,
                    DevicePushNotificationKey = null
                })
        };

        context.SetPermissionContext(new PermissionContext(groups, sharedSecret, keyStoreKey: primaryKeyStoreKey));
        return context;
    }

    private async Task<AppRegistration?> LoadActiveAppAsync(Guid appId)
    {
        var record = await db.AppRegistrations.GetAsync(appId);
        if (record == null)
        {
            return null;
        }

        var app = AppRegistrationService.FromRecord(record);
        return app.AppKeyStore.IsRevoked ? null : app;
    }

    /// <summary>A cached bundle context remembers when its token expires, so expiry needs no database read.</summary>
    private sealed class BundleOdinContext : OdinContext
    {
        public UnixTimeUtc ExpiresAt { get; init; }
    }
}
