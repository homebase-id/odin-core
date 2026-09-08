#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Cryptography.Data;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// Covers <c>CircleNetworkService.ConvertDepositedGrantsForConnectedIdentitiesAsync</c> — the pass
/// <c>VersionUpgradeService</c> runs straight after the master-key pre-pass, draining every deposited
/// grant the owner can reach so the version ladder below it sees grants in one shape rather than two.
/// </summary>
[TestFixture]
public class DepositConversionPrePassTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry];

    [Test]
    public async Task PendingDepositsAcrossConnections_AreAllDrainedInOnePass()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline-sam");
        await PeerFlow.CreatePeerDriveAsync(frodo, merry, DrivePermission.Read, "baseline-merry");

        var (_, circle, app) = await SetupAppWithReadCircleAsync(frodo);
        var client = new V2ConnectionNetworkClient(app.Identity, app.Factory);

        foreach (var target in new[] { sam.Identity, merry.Identity })
        {
            var deposit = await client.GrantCircleAsync(circle, target);
            Assert.That(deposit.IsSuccessStatusCode, Is.True, $"deposit for {target} failed: {deposit.StatusCode}");
        }

        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var storage = scope.Resolve<CircleNetworkStorage>();
        var ctx = await BuildOwnerContextAsync(scope, frodo);

        Assert.That((await storage.GetAsync(sam.Identity))!.PeerKeyStore.HasPendingDeposits, Is.True);
        Assert.That((await storage.GetAsync(merry.Identity))!.PeerKeyStore.HasPendingDeposits, Is.True);

        var (connectionsDrained, grantsConverted) = await scope.Resolve<CircleNetworkService>()
            .ConvertDepositedGrantsForConnectedIdentitiesAsync(ctx, CancellationToken.None);

        Assert.That(connectionsDrained, Is.EqualTo(2), "both connections should have been drained");
        Assert.That(grantsConverted, Is.EqualTo(2), "one grant per connection");

        foreach (var target in new[] { sam.Identity, merry.Identity })
        {
            var icr = await storage.GetAsync(target);
            Assert.That(icr!.PeerKeyStore.HasPendingDeposits, Is.False, $"{target} should have nothing pending");
            Assert.That(icr.PeerKeyStore.CircleGrants.ContainsKey(circle), Is.True,
                $"{target} should now hold a real grant");
        }
    }

    [Test]
    public async Task ConvertedGrant_CarriesRealStorageKey_UnlikeThePeerCatPath()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (_, circle, app) = await SetupAppWithReadCircleAsync(frodo);

        var deposit = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circle, sam.Identity);
        Assert.That(deposit.IsSuccessStatusCode, Is.True, $"deposit failed: {deposit.StatusCode}");

        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var storage = scope.Resolve<CircleNetworkStorage>();
        var ctx = await BuildOwnerContextAsync(scope, frodo);

        await scope.Resolve<CircleNetworkService>()
            .ConvertDepositedGrantsForConnectedIdentitiesAsync(ctx, CancellationToken.None);

        // The owner is present here, so unlike the peer-CAT conversion path -- which has no master key
        // and mints keyless -- the read grant must come out with a usable storage key. Recording the
        // permission without the key would leave sam "in the circle" and able to decrypt nothing.
        var icr = await storage.GetAsync(sam.Identity);
        var driveGrants = icr!.PeerKeyStore.CircleGrants[circle].KeyStoreKeyEncryptedDriveGrants;

        Assert.That(driveGrants.Count, Is.EqualTo(1));
        Assert.That(driveGrants[0].PermissionedDrive.Permission.HasFlag(DrivePermission.Read), Is.True,
            "precondition: this is the read grant that needs a key");
        Assert.That(driveGrants[0].KeyStoreKeyEncryptedStorageKey, Is.Not.Null,
            "a grant converted with the master key present must carry a real storage key");
    }

    [Test]
    public async Task ConnectionRequiringKskUpgrade_IsSkipped_AndItsDepositsStayPending()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (_, circle, app) = await SetupAppWithReadCircleAsync(frodo);
        var deposit = await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circle, sam.Identity);
        Assert.That(deposit.IsSuccessStatusCode, Is.True);

        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var storage = scope.Resolve<CircleNetworkStorage>();
        var ctx = await BuildOwnerContextAsync(scope, frodo);

        // Put the connection back into the pre-upgrade shape: the Peer Key is no longer reachable via
        // the master key, which is exactly the state the master-key pre-pass exists to repair. With no
        // recoverable temp key it cannot be repaired either, so the drain must leave it alone rather
        // than throw.
        var icr = await storage.GetAsync(sam.Identity);
        icr!.PeerKeyStore.MasterKeyEncryptedPeerKey = null!;
        icr.TempWeakKeyStoreKey = null;
        await storage.UpsertAsync(icr, ctx);

        Assert.That((await storage.GetAsync(sam.Identity))!.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade(), Is.True,
            "precondition: the connection should now require the ksk upgrade");

        var (connectionsDrained, grantsConverted) = await scope.Resolve<CircleNetworkService>()
            .ConvertDepositedGrantsForConnectedIdentitiesAsync(ctx, CancellationToken.None);

        Assert.That(connectionsDrained, Is.EqualTo(0), "an unreachable connection should be skipped, not drained");
        Assert.That(grantsConverted, Is.EqualTo(0));

        var after = await storage.GetAsync(sam.Identity);
        Assert.That(after!.PeerKeyStore.HasPendingDeposits, Is.True,
            "the deposit should still be pending, waiting for the connection to become reachable");
    }

    [Test]
    public async Task NoPendingDeposits_IsNoOp()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, frodo);

        var (connectionsDrained, grantsConverted) = await scope.Resolve<CircleNetworkService>()
            .ConvertDepositedGrantsForConnectedIdentitiesAsync(ctx, CancellationToken.None);

        Assert.That(connectionsDrained, Is.EqualTo(0));
        Assert.That(grantsConverted, Is.EqualTo(0));
    }

    [Test]
    public async Task RunningTwice_ConvertsOnceAndThenDoesNothing()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (_, circle, app) = await SetupAppWithReadCircleAsync(frodo);
        await new V2ConnectionNetworkClient(app.Identity, app.Factory).GrantCircleAsync(circle, sam.Identity);

        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var service = scope.Resolve<CircleNetworkService>();
        var ctx = await BuildOwnerContextAsync(scope, frodo);

        var first = await service.ConvertDepositedGrantsForConnectedIdentitiesAsync(ctx, CancellationToken.None);
        Assert.That(first.grantsConverted, Is.EqualTo(1));

        // Safe to re-run: the upgrade flow runs this on every upgrade, not once ever.
        var second = await service.ConvertDepositedGrantsForConnectedIdentitiesAsync(ctx, CancellationToken.None);
        Assert.That(second.connectionsDrained, Is.EqualTo(0));
        Assert.That(second.grantsConverted, Is.EqualTo(0));

        var icr = await scope.Resolve<CircleNetworkStorage>().GetAsync(sam.Identity);
        Assert.That(icr!.PeerKeyStore.CircleGrants.ContainsKey(circle), Is.True, "the grant should still be there");
    }

    /// <summary>
    /// An app that can read a drive, and a circle granting Read on that same drive -- so the app can
    /// source the storage key to seal, but cannot mint the grant itself.  That is what makes this a
    /// deposit rather than a direct mint (see <see cref="WriteOnlyCircleGrantTests"/>).
    /// </summary>
    private static async Task<(TargetDrive drive, Guid circle, AppSession app)> SetupAppWithReadCircleAsync(
        OwnerSession frodo)
    {
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "readDrive", allowAnonymousReads: false);

        var circle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circle, "read-circle", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        });

        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.Read,
            permissionKeys: new[] { PermissionKeys.ManageCircleMembership });

        return (drive, circle, app);
    }

    private async Task<IOdinContext> BuildOwnerContextAsync(ILifetimeScope scope, OwnerSession owner)
    {
        var authService = scope.Resolve<OwnerAuthenticationService>();
        var odinContext = new OdinContext { Tenant = default, AuthTokenCreated = null, Caller = null };
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
