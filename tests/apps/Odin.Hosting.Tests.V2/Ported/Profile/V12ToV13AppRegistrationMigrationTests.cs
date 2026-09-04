using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version12tov13;

namespace Odin.Hosting.Tests.V2.Ported.Profile;

/// <summary>
/// Verifies that v12 -> v13 actually reads the app registrations sitting in the key-three-value blob.
/// </summary>
/// <remarks>
/// The failure this pins was silent. <see cref="AppRegistration"/> <c>[JsonIgnore]</c>s AppId, AppSlug,
/// Name and CorsHostName because they are columns now -- correct for writing, and fatal for reading a
/// legacy row, where the blob JSON is the only place those values exist. Deserializing the blob into the
/// current type yields an AppId of null for every app, the migration reports "nothing to move", commits,
/// and the identity is left with no apps at all while the blob still holds them.
/// <para>
/// So the assertion that matters is not "the migration ran" but "the app arrived with its real name and a
/// real slug". A migration that reads nothing passes every check except this one.
/// </para>
/// </remarks>
[TestFixture]
public class V12ToV13AppRegistrationMigrationTests : V2Fixture
{
    // The context and category keys AppRegistrationService used while registrations lived in the blob.
    private static readonly ThreeKeyValueStorage LegacyAppRegStorage =
        TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse("661e097f-6aa5-459f-a445-a9ea65348fde"));

    private static readonly byte[] LegacyAppRegDataType =
        Guid.Parse("14c83583-acfd-4368-89ad-6566636ace3d").ToByteArray();

    /// <summary>
    /// The blob shape as it was written before the columns were promoted: AppId, Name and CorsHostName
    /// are ordinary serialized properties here, which is exactly what the current type can no longer read.
    /// </summary>
    private class LegacyAppRegistration
    {
        public GuidId AppId { get; set; }
        public string Name { get; set; }
        public List<Guid> AuthorizedCircles { get; set; }
        public PermissionSetGrantRequest CircleMemberPermissionGrant { get; set; }

        [JsonPropertyName("grant")]
        public KeyStore AppKeyStore { get; set; }

        public string CorsHostName { get; set; }
    }

    [Test]
    public async Task V13_MovesBlobAppRegistrations_WithTheirNamesAndSlugs()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var db = scope.Resolve<IdentityDatabase>();
        var tblKeyThreeValue = scope.Resolve<TableKeyThreeValueCached>();

        var appId = Guid.NewGuid();
        var legacy = new LegacyAppRegistration
        {
            AppId = appId,
            Name = "Acme Receipts",
            CorsHostName = "acme.example.com",
            AuthorizedCircles = [],
            CircleMemberPermissionGrant = new PermissionSetGrantRequest
            {
                PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
            },
            AppKeyStore = null
        };

        await LegacyAppRegStorage.UpsertAsync(tblKeyThreeValue, appId, GuidId.Empty, LegacyAppRegDataType, legacy);

        // The app is in the blob and not in the table: exactly the state every pre-v13 identity is in.
        Assert.That(await db.AppRegistrations.GetAsync(appId), Is.Null);

        var migration = scope.Resolve<V12ToV13VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        var record = await db.AppRegistrations.GetAsync(appId);

        Assert.That(record, Is.Not.Null, "the blob registration must land in the AppRegistrations table");
        Assert.That(record.Name, Is.EqualTo("Acme Receipts"), "the name lives only in the blob JSON");
        Assert.That(record.CorsHostName, Is.EqualTo("acme.example.com"));
        Assert.That(AppSlugGenerator.IsValid(record.AppSlug), Is.True, $"'{record.AppSlug}' is not a valid slug");

        // Derived from the display name, not the fallback: a null Name would have produced the app id.
        // Fits under the 14-character cap, so "acme-receipts" survives whole.
        Assert.That(record.AppSlug, Is.EqualTo("acme-receipts"));
    }

    [Test]
    public async Task V13_IsIdempotent_AndDoesNotReassignASlug()
    {
        var owner = await LoginAsOwner(Identities.Sam);

        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var db = scope.Resolve<IdentityDatabase>();
        var tblKeyThreeValue = scope.Resolve<TableKeyThreeValueCached>();

        var appId = Guid.NewGuid();
        await LegacyAppRegStorage.UpsertAsync(tblKeyThreeValue, appId, GuidId.Empty, LegacyAppRegDataType,
            new LegacyAppRegistration
            {
                AppId = appId,
                Name = "Repeatable",
                AuthorizedCircles = [],
                CircleMemberPermissionGrant = new PermissionSetGrantRequest()
            });

        var migration = scope.Resolve<V12ToV13VersionMigrationService>();

        await migration.UpgradeAsync(ctx, CancellationToken.None);
        var first = await db.AppRegistrations.GetAsync(appId);

        // A partial run has to be repeatable, and the slug is a wire address other identities may
        // already hold -- a second pass must not mint a new one.
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);
        var second = await db.AppRegistrations.GetAsync(appId);

        Assert.That(second, Is.Not.Null);
        Assert.That(second.AppSlug, Is.EqualTo(first.AppSlug));
    }

    private async Task<IOdinContext> BuildOwnerContextAsync(ILifetimeScope scope, OwnerSession owner)
    {
        var authService = scope.Resolve<OwnerAuthenticationService>();
        var odinContext = new OdinContext
        {
            Tenant = default,
            AuthTokenCreated = null,
            Caller = null
        };
        var clientContext = new OdinClientContext
        {
            CorsHostName = null,
            AccessRegistrationId = null,
            DevicePushNotificationKey = null,
            ClientIdOrDomain = null
        };

        await authService.UpdateOdinContextAsync(owner.Token, clientContext, odinContext);
        odinContext.Caller.AssertHasMasterKey();
        return odinContext;
    }
}
