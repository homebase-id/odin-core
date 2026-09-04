using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps.Builtin;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Drives.Management;

namespace Odin.Hosting.Tests.V2.Addressing;

/// <summary>
/// Every drive is minted a write-only keypair at creation: an ECC-384 key whose private half is
/// escrowed under that drive's own storage key (docs/drive-addressing.md).
/// </summary>
/// <remarks>
/// Nothing serves the public half yet -- the peer <c>public-key</c> endpoint is not built -- so these
/// assert against the drive record in process rather than over HTTP.  That is the point: the key has
/// to be there and stay there long before anything reads it, or the endpoint arrives to find a
/// decade of drives with nothing to return.
/// </remarks>
[TestFixture]
public class DriveWriteOnlyKeyPairTests : V2Fixture
{
    /// <summary>
    /// Reads the drive straight from the tenant's DriveManager.  <c>GetDriveAsync</c> takes no
    /// OdinContext, so this needs no caller identity -- and the keypair is not exposed over any API to
    /// read it through.
    /// </summary>
    private async Task<StorageDrive> ReadDriveAsync(OwnerSession owner, TargetDrive drive)
    {
        var scope = owner.Host.GetTenantScope(owner.Identity.DomainName);
        var driveManager = scope.Resolve<IDriveManager>();
        return await driveManager.GetDriveAsync(drive.Alias, failIfInvalid: true);
    }

    [Test]
    public async Task ADriveIsMintedAKeypairWhenItIsCreated()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "minted");

        var stored = await ReadDriveAsync(owner, drive);

        Assert.That(stored.WriteOnlyKeyPair, Is.Not.Null);
        Assert.That(stored.WriteOnlyKeyPair.publicKey, Is.Not.Null.And.Not.Empty);
    }

    /// <summary>
    /// The drive's real storage key, taken from the caller's own grant -- the same key
    /// <c>DriveManager</c> escrows the private half under.
    /// </summary>
    private async Task<SensitiveByteArray> DriveStorageKeyAsync(OwnerSession owner, TargetDrive drive)
    {
        var scope = owner.Host.GetTenantScope(owner.Identity.DomainName);
        var odinContext = await BuildOwnerContextAsync(scope, owner);
        return odinContext.PermissionsContext.GetDriveStorageKey(drive.Alias);
    }

    [Test]
    public async Task ThePrivateHalfIsEscrowedUnderThatDrivesStorageKey()
    {
        // The security claim the whole design rests on: deposit-collection custody equals existing read
        // access. That holds only if the private half opens with the key that grants access to THIS
        // drive -- not merely with "some" symmetric key, which is all a unit test can show.
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "escrowed");

        var stored = await ReadDriveAsync(owner, drive);
        var storageKey = await DriveStorageKeyAsync(owner, drive);

        Assert.That(() => stored.WriteOnlyKeyPair.privateDerBase64(storageKey), Throws.Nothing,
            "the drive's own storage key must open its keypair");
    }

    [Test]
    public async Task AnotherDrivesStorageKeyDoesNotOpenIt()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        var other = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "escrowed");
        await owner.Admin.CreateDrive(other, "other");

        var stored = await ReadDriveAsync(owner, drive);
        var otherStored = await ReadDriveAsync(owner, other);
        var otherStorageKey = await DriveStorageKeyAsync(owner, other);

        Assert.That(stored.WriteOnlyKeyPair.publicKey, Is.Not.EqualTo(otherStored.WriteOnlyKeyPair.publicKey));
        Assert.That(() => stored.WriteOnlyKeyPair.privateDerBase64(otherStorageKey), Throws.Exception,
            "access to one drive must not open another drive's deposits");
    }

    [Test]
    public async Task TheMasterKeyDoesNotOpenIt()
    {
        // The escrow key is the drive's storage key, which CreateDriveAsync generates fresh
        // (new SymmetricKeyEncryptedAes(mk)) rather than deriving from the master key. If the master
        // key opened the keypair, the escrow would not be saying what it claims to say.
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "escrowed");

        var stored = await ReadDriveAsync(owner, drive);

        var scope = owner.Host.GetTenantScope(owner.Identity.DomainName);
        var odinContext = await BuildOwnerContextAsync(scope, owner);
        var masterKey = odinContext.Caller.GetMasterKey();

        Assert.That(() => stored.WriteOnlyKeyPair.privateDerBase64(masterKey), Throws.Exception);
    }

    [Test]
    public async Task ADepositSealedToThePublicHalfIsUnsealedWithTheDriveStorageKey()
    {
        // End to end, the thing the key exists for: a caller holding only the public half seals a
        // payload, and only the drive storage key recovers it. Uses the connection-level Seal/Unseal
        // because the mechanics are identical and no drive-specific pair exists yet -- what is being
        // exercised is the escrow, not the envelope format.
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "deposit");

        var stored = await ReadDriveAsync(owner, drive);
        var storageKey = await DriveStorageKeyAsync(owner, drive);

        var secret = "a receipt nobody but the drive owner may read"u8.ToArray();
        var publicHalf = EccPublicKeyData.FromJwkPublicKey(stored.WriteOnlyKeyPair.PublicKeyJwk());
        var envelope = PeerKeyStoreWriteOnlyKey.Seal(publicHalf, secret);

        var recovered = PeerKeyStoreWriteOnlyKey.Unseal(stored.WriteOnlyKeyPair, storageKey, envelope);

        Assert.That(recovered, Is.EqualTo(secret));
    }

    [Test]
    public async Task ADepositStaysReadableAcrossEveryDriveUpdatePath()
    {
        // The consequence the never-overwrite and never-drop rules exist to protect. Asserting the
        // public half is unchanged says the bytes match; this says the deposit still opens, which is
        // what actually breaks if a write path rebuilds or drops the key.
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "durable");

        var before = await ReadDriveAsync(owner, drive);
        var secret = "sealed before any of the updates below"u8.ToArray();
        var envelope = PeerKeyStoreWriteOnlyKey.Seal(
            EccPublicKeyData.FromJwkPublicKey(before.WriteOnlyKeyPair.PublicKeyJwk()), secret);

        var scope = owner.Host.GetTenantScope(owner.Identity.DomainName);
        var driveManager = scope.Resolve<IDriveManager>();
        var odinContext = await BuildOwnerContextAsync(scope, owner);

        // Every write path that goes through ToRecord.
        await owner.Admin.SetAllowCdn(drive, true);
        await driveManager.UpdateMetadataAsync(drive.Alias, "changed", odinContext);
        await driveManager.UpdateAttributesAsync(drive.Alias,
            new System.Collections.Generic.Dictionary<string, string> { { "k", "v" } }, odinContext);
        await driveManager.SetDriveAllowSubscriptionsAsync(drive.Alias, true, odinContext);
        await driveManager.SetDriveReadModeAsync(drive.Alias, true, odinContext);
        await owner.Admin.SetArchiveFlag(drive, true);

        var after = await ReadDriveAsync(owner, drive);
        var storageKey = await DriveStorageKeyAsync(owner, drive);

        Assert.That(after.WriteOnlyKeyPair, Is.Not.Null, "no update path may drop the keypair");
        Assert.That(PeerKeyStoreWriteOnlyKey.Unseal(after.WriteOnlyKeyPair, storageKey, envelope),
            Is.EqualTo(secret), "a deposit sealed before the updates must still open after them");
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

    [Test]
    public async Task EveryDriveGetsADistinctKeypair()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var a = TargetDrive.NewTargetDrive();
        var b = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(a, "a");
        await owner.Admin.CreateDrive(b, "b");

        var driveA = await ReadDriveAsync(owner, a);
        var driveB = await ReadDriveAsync(owner, b);

        Assert.That(driveA.WriteOnlyKeyPair.publicKey, Is.Not.EqualTo(driveB.WriteOnlyKeyPair.publicKey));
    }

    [Test]
    public async Task TheKeypairSurvivesADriveUpdate()
    {
        // The regression this suite exists for. ToRecord backs every drive upsert, and it used to omit
        // WriteOnlyKeyPair -- so any flag change wrote NULL over the key. Invisible while nothing
        // minted one; silent, permanent key loss now that creation does. Deposits sealed to the old
        // public half would have become unopenable with no error anywhere.
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "survives");

        var before = await ReadDriveAsync(owner, drive);
        Assert.That(before.WriteOnlyKeyPair, Is.Not.Null);

        await owner.Admin.SetAllowCdn(drive, true);

        var after = await ReadDriveAsync(owner, drive);
        Assert.That(after.WriteOnlyKeyPair, Is.Not.Null, "a drive update must not drop the keypair");
        Assert.That(after.WriteOnlyKeyPair.publicKey, Is.EqualTo(before.WriteOnlyKeyPair.publicKey),
            "and must not silently mint a new one either -- deposits are sealed to the old public half");
    }

    [Test]
    public async Task TheKeypairSurvivesArchiving()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "archived");

        var before = await ReadDriveAsync(owner, drive);
        await owner.Admin.SetArchiveFlag(drive, true);
        var after = await ReadDriveAsync(owner, drive);

        Assert.That(after.WriteOnlyKeyPair?.publicKey, Is.EqualTo(before.WriteOnlyKeyPair.publicKey));
    }

    [Test]
    public async Task TheDrivesTheTreeProvisionsAreAlsoMinted()
    {
        // Identity setup goes through the same CreateDriveAsync, so the built-in drives should be
        // covered without anything special -- worth asserting, since they are the drives a stranger is
        // most likely to deposit to.
        var owner = await LoginAsOwner(Identities.Frodo);
        var scope = owner.Host.GetTenantScope(owner.Identity.DomainName);
        var driveManager = scope.Resolve<IDriveManager>();

        // Asked one drive at a time: the paged read needs a caller context to filter on, and this
        // assertion is about storage, not visibility.
        var missing = new System.Collections.Generic.List<string>();
        foreach (var seeded in BuiltinApps.SeededDrives)
        {
            var stored = await driveManager.GetDriveAsync(seeded.TargetDrive.Alias, failIfInvalid: true);
            if (stored.WriteOnlyKeyPair == null)
            {
                missing.Add(stored.Name);
            }
        }

        Assert.That(missing, Is.Empty, $"drives without a keypair: {string.Join(", ", missing)}");
    }
}
