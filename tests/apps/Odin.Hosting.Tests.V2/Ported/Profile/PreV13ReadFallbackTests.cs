using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade.Version12tov13;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.V2.Ported.Profile;

/// <summary>
/// An identity that has not run v12 -&gt; v13 still answers with the apps and circles it has.
/// </summary>
/// <remarks>
/// The upgrade only runs when the owner logs in, so between a deploy and that login the tables are
/// empty while the services read nothing else.  Without the fallback such an identity has no apps and
/// no circles: app clients cannot authenticate, and <c>IsEnabledAsync</c> reports every circle
/// disabled because a null definition is not a disabled one.
/// <para>
/// The last test is the point of the version gate.  The move deliberately leaves the blob rows in
/// place, so falling back on "the table has no such row" would resurrect anything the owner deleted
/// afterwards.  Only the version can tell those two states apart.
/// </para>
/// </remarks>
[TestFixture]
public class PreV13ReadFallbackTests : V2Fixture
{
    // The context and category keys the services used while definitions lived in the blob.
    private static readonly ThreeKeyValueStorage LegacyAppRegStorage =
        TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse("661e097f-6aa5-459f-a445-a9ea65348fde"));

    private static readonly byte[] LegacyAppRegDataType =
        Guid.Parse("14c83583-acfd-4368-89ad-6566636ace3d").ToByteArray();

    private static readonly ThreeKeyValueStorage LegacyCircleStorage =
        TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse("dc1c198c-c280-4b9c-93ce-d417d0a58491"));

    private static readonly byte[] LegacyCircleDataType =
        Guid.Parse("2a915ab8-412e-42d8-b157-a123f107f224").ToByteArray();

    /// <summary>
    /// The blob shape as it was written before the columns were promoted.  The current
    /// <see cref="AppRegistration"/> cannot read it -- that is the whole reason the frozen copy exists.
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
    public async Task PreV13_AnAppLivingOnlyInTheBlob_IsStillFound()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var db = scope.Resolve<IdentityDatabase>();

        var appId = Guid.NewGuid();
        await SeedLegacyAppAsync(scope, appId, "Acme Receipts");
        await SetVersionAsync(scope, 12);

        // The state every pre-v13 identity is in: in the blob, not in the table.
        Assert.That(await db.AppRegistrations.GetAsync(appId), Is.Null);

        var apps = scope.Resolve<IAppRegistrationService>();

        var single = await apps.GetAppRegistration(appId, ctx);
        Assert.That(single, Is.Not.Null, "app-token auth resolves through this path");
        Assert.That(single.Name, Is.EqualTo("Acme Receipts"));
        Assert.That(AppSlugGenerator.IsValid(single.AppSlug), Is.True,
            "the slug must be the one the move will coin, so the address does not change on upgrade");
        Assert.That(single.AppSlug, Is.EqualTo("acme-receipts"));

        var all = await apps.GetRegisteredAppsAsync(ctx);
        Assert.That(all.Any(a => (Guid)a.AppId == appId), Is.True, "the list must include blob apps");
    }

    [Test]
    public async Task PreV13_ACircleLivingOnlyInTheBlob_IsStillFoundAndEnabled()
    {
        var owner = await LoginAsOwner(Identities.Sam);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var db = scope.Resolve<IdentityDatabase>();

        var circleId = Guid.NewGuid();
        await SeedLegacyCircleAsync(scope, circleId, "Book Club");
        await SetVersionAsync(scope, 12);

        Assert.That(await db.CircleCached.GetAsync(circleId), Is.Null);

        var circles = scope.Resolve<CircleDefinitionService>();

        var single = await circles.GetCircleAsync(circleId);
        Assert.That(single, Is.Not.Null);
        Assert.That(single.Name, Is.EqualTo("Book Club"));

        // A null definition is not a disabled one, but IsEnabledAsync cannot tell the difference --
        // which is how a missing circle silently revokes what it grants.
        Assert.That(await circles.IsEnabledAsync(circleId), Is.True);

        var all = await circles.GetCirclesAsync(includeSystemCircle: true);
        Assert.That(all.Any(c => (Guid)c.Id == circleId), Is.True, "the list must include blob circles");
    }

    [Test]
    public async Task PostV13_TheBlobIsNotConsulted_SoADeletedAppStaysDeleted()
    {
        var owner = await LoginAsOwner(Identities.Merry);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);

        var appId = Guid.NewGuid();
        var circleId = Guid.NewGuid();
        await SeedLegacyAppAsync(scope, appId, "Ghost");
        await SeedLegacyCircleAsync(scope, circleId, "Ghost Circle");

        // The move does not delete blob rows, so this is exactly the shape of an identity that has
        // migrated and then deleted the app: gone from the table, still sitting in the blob.
        await SetVersionAsync(scope, LegacyDefinitionStore.MovedInVersion);

        var apps = scope.Resolve<IAppRegistrationService>();
        var circles = scope.Resolve<CircleDefinitionService>();

        Assert.That(await apps.GetAppRegistration(appId, ctx), Is.Null,
            "a migrated identity must not resurrect a deleted app from the blob it left behind");
        Assert.That((await apps.GetRegisteredAppsAsync(ctx)).Any(a => (Guid)a.AppId == appId), Is.False);

        Assert.That(await circles.GetCircleAsync(circleId), Is.Null);
        Assert.That((await circles.GetCirclesAsync(includeSystemCircle: true)).Any(c => (Guid)c.Id == circleId),
            Is.False);
    }

    private static async Task SeedLegacyAppAsync(ILifetimeScope scope, Guid appId, string name)
    {
        var tblKeyThreeValue = scope.Resolve<TableKeyThreeValueCached>();
        await LegacyAppRegStorage.UpsertAsync(tblKeyThreeValue, appId, GuidId.Empty, LegacyAppRegDataType,
            new LegacyAppRegistration
            {
                AppId = appId,
                Name = name,
                AuthorizedCircles = [],
                CircleMemberPermissionGrant = new PermissionSetGrantRequest
                {
                    PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
                },

                // A real blob row always carries its grant -- Redacted() reads IsRevoked, Created and
                // Modified straight off it -- so a seeded row without one would be testing a shape
                // that never existed.
                AppKeyStore = new KeyStore
                {
                    Created = UnixTimeUtc.Now(),
                    Modified = UnixTimeUtc.Now(),
                    IsRevoked = false,
                    DriveGrants = [],
                    PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
                }
            });
    }

    private static async Task SeedLegacyCircleAsync(ILifetimeScope scope, Guid circleId, string name)
    {
        var tblKeyThreeValue = scope.Resolve<TableKeyThreeValueCached>();
        await LegacyCircleStorage.UpsertAsync(tblKeyThreeValue, circleId, GuidId.Empty, LegacyCircleDataType,
            new CircleDefinition
            {
                Id = circleId,
                Name = name,
                Description = "seeded into the blob",
                DriveGrants = [],
                Permissions = new PermissionSet()
            });
    }

    private static async Task SetVersionAsync(ILifetimeScope scope, int version)
    {
        await scope.Resolve<TenantConfigService>().ForceVersionNumberAsync(version);
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
