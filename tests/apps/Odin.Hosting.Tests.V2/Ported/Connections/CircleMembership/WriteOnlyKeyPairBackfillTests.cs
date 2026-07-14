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
using Odin.Services.EncryptionKeyService;
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

    [Test]
    public async Task ConnectionRequiringKskUpgrade_WithRecoverableTempKey_UpgradesAndBackfillsKeypair_InOnePass()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var storage = scope.Resolve<CircleNetworkStorage>();
        var keyService = scope.Resolve<PublicPrivateKeyService>();
        var ctx = await BuildOwnerContextAsync(scope, frodo);
        var masterKey = ctx.Caller!.GetMasterKey();

        var before = await storage.GetAsync(sam.Identity);
        Assert.That(before!.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade(), Is.False,
            "precondition: normal accept flow should already carry master-key store-key encryption");

        // Recover the real key store key while it's still reachable via the master key, then reseal
        // it as the "weak" ECC-encrypted fallback (TempWeakKeyStoreKey) the same way the accept flow
        // does for connections established without the owner online — this simulates a connection
        // that predates master-key encryption but CAN still be recovered, as opposed to one that
        // can't (see the sibling test below).
        var recoveredKeyStoreKey = before.PeerKeyStore.MasterKeyEncryptedPeerKey.DecryptKeyClone(masterKey);
        var resealedTempKey = await keyService.EccEncryptPayload(
            PublicPrivateKeyType.OnlineIcrEncryptedKey, recoveredKeyStoreKey.GetKey());

        before.TempWeakKeyStoreKey = resealedTempKey;
        before.PeerKeyStore.MasterKeyEncryptedPeerKey = null;
        before.PeerKeyStore.WriteOnlyKeyPair = null;
        await storage.UpsertAsync(before, ctx);

        var midpoint = await storage.GetAsync(sam.Identity);
        Assert.That(midpoint!.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade(), Is.True,
            "precondition: simulated pre-migration state should require the KSK upgrade");

        var circleNetworkService = scope.Resolve<CircleNetworkService>();
        var (upgraded, skipped, keyPairsProvisioned) =
            await circleNetworkService.UpgradeMasterKeyStoreKeyEncryptionForConnectedIdentitiesAsync(ctx, CancellationToken.None);

        Assert.That(upgraded, Is.EqualTo(1), "the KSK upgrade should have succeeded using the recoverable temp key");
        Assert.That(keyPairsProvisioned, Is.EqualTo(1),
            "the write-only keypair should be backfilled in the SAME pass once the KSK upgrade makes the key store key reachable again");
        Assert.That(skipped, Is.EqualTo(0));

        var after = await storage.GetAsync(sam.Identity);
        Assert.That(after!.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade(), Is.False,
            "master-key store-key encryption should be restored");
        Assert.That(after.PeerKeyStore.WriteOnlyKeyPair, Is.Not.Null, "the write-only keypair should now be provisioned");

        var restoredKeyStoreKey = after.PeerKeyStore.MasterKeyEncryptedPeerKey.DecryptKeyClone(masterKey);
        Assert.That(restoredKeyStoreKey.GetKey(), Is.EqualTo(recoveredKeyStoreKey.GetKey()),
            "the upgrade should recover the SAME underlying key store key, not mint a new one");
    }

    [Test]
    public async Task ConnectionRequiringKskUpgrade_WithNoRecoverableTempKey_IsSkipped_KeypairNotTouched()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var storage = scope.Resolve<CircleNetworkStorage>();
        var ctx = await BuildOwnerContextAsync(scope, frodo);

        // Normal owner-accepted connections never populate TempWeakKeyStoreKey (only accepts without
        // the owner online do) — nulling MasterKeyEncryptedPeerKey alone reproduces an identity whose
        // KSK upgrade genuinely cannot be recovered, e.g. a corrupted or never-populated fallback.
        var before = await storage.GetAsync(sam.Identity);
        Assert.That(before!.TempWeakKeyStoreKey, Is.Null,
            "precondition: a normal owner-accepted connection has no weak fallback key to begin with");
        before.PeerKeyStore.MasterKeyEncryptedPeerKey = null;
        await storage.UpsertAsync(before, ctx);

        var circleNetworkService = scope.Resolve<CircleNetworkService>();
        var (upgraded, skipped, keyPairsProvisioned) =
            await circleNetworkService.UpgradeMasterKeyStoreKeyEncryptionForConnectedIdentitiesAsync(ctx, CancellationToken.None);

        Assert.That(upgraded, Is.EqualTo(0), "the KSK upgrade cannot succeed without a recoverable temp key");
        Assert.That(skipped, Is.EqualTo(1), "the identity should be left for lazy reconcile rather than crash the pre-pass");
        Assert.That(keyPairsProvisioned, Is.EqualTo(0),
            "the keypair backfill must not run for an identity whose key store key is still unreachable");

        var after = await storage.GetAsync(sam.Identity);
        Assert.That(after!.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade(), Is.True,
            "the identity should still require the upgrade — nothing was silently fabricated");
        Assert.That(after.PeerKeyStore.WriteOnlyKeyPair, Is.Not.Null,
            "the pre-existing (real) write-only keypair from the normal accept flow must be left untouched");
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
