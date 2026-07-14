#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// Covers <c>CircleNetworkService.UpgradeMasterKeyStoreKeyEncryptionForConnectedIdentitiesAsync</c> —
/// the unconditional pre-pass <c>VersionUpgradeService.UpgradeAsync</c> runs before the version ladder,
/// which (in addition to the pre-existing master-key store-key encryption upgrade) now also backfills
/// <see cref="Odin.Services.Membership.Connections.PeerKeyStore.WriteOnlyKeyPair"/> for connected
/// identities that predate it.
/// </summary>
[TestFixture]
public class WriteOnlyKeyPairBackfillTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task ConnectionMissingWriteOnlyKeyPair_IsBackfilled()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var storage = scope.Resolve<CircleNetworkStorage>();
        var ctx = await BuildOwnerContextAsync(scope, frodo);

        var before = await storage.GetAsync(sam.Identity);
        Assert.That(before!.PeerKeyStore.WriteOnlyKeyPair, Is.Not.Null,
            "precondition: normal accept flow should have provisioned the write-only keypair");
        Assert.That(before.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade(), Is.False,
            "precondition: normal accept flow should already carry master-key store-key encryption");

        before.PeerKeyStore.WriteOnlyKeyPair = null;
        await storage.UpsertAsync(before, ctx);

        var circleNetworkService = scope.Resolve<CircleNetworkService>();
        var (upgraded, skipped, keyPairsProvisioned) =
            await circleNetworkService.UpgradeMasterKeyStoreKeyEncryptionForConnectedIdentitiesAsync(ctx, CancellationToken.None);

        Assert.That(keyPairsProvisioned, Is.EqualTo(1), "exactly one identity should have had its keypair backfilled");
        Assert.That(upgraded, Is.EqualTo(0), "no KSK-encryption upgrade was needed");
        Assert.That(skipped, Is.EqualTo(0), "no identity should have been skipped");

        var after = await storage.GetAsync(sam.Identity);
        Assert.That(after!.PeerKeyStore.WriteOnlyKeyPair, Is.Not.Null,
            "the write-only keypair should now be provisioned");
    }

    [Test]
    public async Task ConnectionAlreadyFullyProvisioned_IsNoOp()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, frodo);

        var storage = scope.Resolve<CircleNetworkStorage>();
        var before = await storage.GetAsync(sam.Identity);
        Assert.That(before!.PeerKeyStore.WriteOnlyKeyPair, Is.Not.Null,
            "precondition: normal accept flow should already have provisioned the write-only keypair");

        var circleNetworkService = scope.Resolve<CircleNetworkService>();
        var (upgraded, skipped, keyPairsProvisioned) =
            await circleNetworkService.UpgradeMasterKeyStoreKeyEncryptionForConnectedIdentitiesAsync(ctx, CancellationToken.None);

        Assert.That(keyPairsProvisioned, Is.EqualTo(0), "nothing should be backfilled for an already-provisioned identity");
        Assert.That(upgraded, Is.EqualTo(0));
        Assert.That(skipped, Is.EqualTo(0));
    }

    /// <summary>
    /// Builds an owner context carrying the master key by replaying the production path used by
    /// <c>VersionUpgradeService</c> (<see cref="OwnerAuthenticationService.UpdateOdinContextAsync"/>).
    /// </summary>
    private async Task<IOdinContext> BuildOwnerContextAsync(Autofac.ILifetimeScope scope, OwnerSession owner)
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
        odinContext.Caller!.AssertHasMasterKey();
        return odinContext;
    }
}
