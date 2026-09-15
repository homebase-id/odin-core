using System;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Connections;
using Refit;

namespace Odin.Hosting.Tests.V2.Addressing;

/// <summary>
/// Fetching a drive's write-only public key over peer:
/// <c>GET /api/v2/peer/{odinId}/apps/{appSlug}/drives/{driveSlug}/public-key</c>.
/// </summary>
/// <remarks>
/// The key exists so a caller can write to a drive it cannot read -- seal to the public half and only
/// a holder of that drive's storage key can open it (docs/drive-addressing.md).  So the gate is Write
/// on the drive, and the test that matters is not "a key came back" but that the key which came back
/// is the one the drive actually holds: sealed to it, the owner can open it.
/// </remarks>
[TestFixture]
public class PeerAppDrivePublicKeyTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private const string AppSlug = "ledger";
    private const string DriveSlug = "inbox";

    /// <summary>
    /// Sam hosts an app-owned drive; Frodo is connected with <paramref name="frodoPermission"/> on it.
    /// </summary>
    private async Task<(OwnerSession frodo, OwnerSession sam, TargetDrive drive)> SetupAsync(
        DrivePermission frodoPermission)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var appId = Guid.NewGuid();
        var drive = TargetDrive.NewTargetDrive();

        await sam.Admin.RegisterApp(appId, new PermissionSetGrantRequest(), appSlug: AppSlug);
        await sam.Admin.CreateDrive(drive, "Sam's inbox", allowAnonymousReads: false, appId: appId,
            driveSlug: DriveSlug, driveTypeSlug: "inbox");

        await PeerFlow.ConnectAsync(frodo, sam, drive, frodoPermission);

        return (frodo, sam, drive);
    }

    private static IPeerAppDriveHttpClientApiV2 SlugClient(OwnerSession owner)
    {
        // Shared-secret aware, like the other slug read routes: a GET response is encrypted with the
        // caller's shared secret, and a plain client deserializes it to an object of nulls.
        var (client, ss) = owner.NewAdminHttpClient();
        return RefitCreator.RestServiceFor<IPeerAppDriveHttpClientApiV2>(client, ss);
    }

    /// <summary>Sam's copy of the drive, read straight from his DriveManager.</summary>
    private async Task<StorageDrive> SamsDriveAsync(OwnerSession sam, TargetDrive drive)
    {
        var scope = sam.Host.GetTenantScope(sam.Identity.DomainName);
        return await scope.Resolve<IDriveManager>().GetDriveAsync(drive.Alias, failIfInvalid: true);
    }

    [Test]
    public async Task AWriterGetsTheKeyTheDriveActuallyHolds()
    {
        // The assertion worth having. "A key came back" would pass against any key at all; this seals
        // to what the endpoint served and has the drive's owner open it, which can only work if the
        // served key is the drive's own and its private half is escrowed under that drive's storage key.
        var (frodo, sam, drive) = await SetupAsync(DrivePermission.Write);

        var response = await SlugClient(frodo).GetDriveWriteOnlyPublicKey(sam.Identity.DomainName, AppSlug, DriveSlug);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"expected 200, got {response.StatusCode}");
        Assert.That(response.Content!.PublicKeyJwk, Is.Not.Null.And.Not.Empty);

        var secret = "a receipt Frodo may write but never read"u8.ToArray();
        var envelope = PeerKeyStoreWriteOnlyKey.Seal(
            EccPublicKeyData.FromJwkPublicKey(response.Content.PublicKeyJwk), secret);

        var samsDrive = await SamsDriveAsync(sam, drive);
        var scope = sam.Host.GetTenantScope(sam.Identity.DomainName);
        var samContext = await BuildOwnerContextAsync(scope, sam);
        var storageKey = samContext.PermissionsContext.GetDriveStorageKey(samsDrive.Id);

        Assert.That(PeerKeyStoreWriteOnlyKey.Unseal(samsDrive.WriteOnlyKeyPair, storageKey, envelope),
            Is.EqualTo(secret), "the served key must be the one the drive holds");
        Assert.That(response.Content.PublicKeyCrc32, Is.EqualTo(samsDrive.WriteOnlyKeyPair.crc32c));
    }

    [Test]
    public async Task AReaderIsRefused()
    {
        // Read is not enough, deliberately: this key is the means to put data on the drive, and a
        // reader has no business with it. Unlike address resolution, which accepts any grant.
        var (frodo, sam, _) = await SetupAsync(DrivePermission.Read);

        var response = await SlugClient(frodo).GetDriveWriteOnlyPublicKey(sam.Identity.DomainName, AppSlug, DriveSlug);

        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "a caller without Write is refused, not told the drive is missing");
    }

    [Test]
    public async Task AnUnknownDriveSlugIsNotFound()
    {
        var (frodo, sam, _) = await SetupAsync(DrivePermission.Write);

        var response = await SlugClient(frodo).GetDriveWriteOnlyPublicKey(sam.Identity.DomainName, AppSlug, "nope");

        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task AnUnknownAppSlugIsNotFound()
    {
        var (frodo, sam, _) = await SetupAsync(DrivePermission.Write);

        var response = await SlugClient(frodo).GetDriveWriteOnlyPublicKey(sam.Identity.DomainName, "nope", DriveSlug);

        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
    }

    private async Task<Odin.Services.Base.IOdinContext> BuildOwnerContextAsync(ILifetimeScope scope, OwnerSession owner)
    {
        var authService = scope.Resolve<Odin.Services.Authentication.Owner.OwnerAuthenticationService>();
        var odinContext = new Odin.Services.Base.OdinContext
        {
            Tenant = default,
            AuthTokenCreated = null,
            Caller = null
        };
        var clientContext = new Odin.Services.Base.OdinClientContext
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
