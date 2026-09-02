using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps.Builtin;
using Odin.Services.Drives;
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

    [Test]
    public async Task ThePrivateHalfIsEscrowedUnderThatDrivesStorageKey()
    {
        // The security claim: deposit-collection custody equals existing read access. If the private
        // half opened with anything else, that equality would not hold.
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "escrowed");

        var stored = await ReadDriveAsync(owner, drive);

        var scope = owner.Host.GetTenantScope(owner.Identity.DomainName);
        var driveManager = scope.Resolve<IDriveManager>();
        var other = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(other, "other");
        var otherDrive = await driveManager.GetDriveAsync(other.Alias, failIfInvalid: true);

        // Each drive's key is its own; one drive's storage key must not open another's.
        Assert.That(stored.WriteOnlyKeyPair.publicKey,
            Is.Not.EqualTo(otherDrive.WriteOnlyKeyPair.publicKey));
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
