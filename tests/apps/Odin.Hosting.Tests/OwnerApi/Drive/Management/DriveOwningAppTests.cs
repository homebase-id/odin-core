using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Apps.Builtin;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;

namespace Odin.Hosting.Tests.OwnerApi.Drive.Management;

/// <summary>
/// Adoption: giving a drive that belongs to no app an owning app, and the address that goes with it.
///
/// Every drive predating the addressing work carries a null AppId, which reads as "the owner's own"
/// and leaves it unaddressable by slug.  Adoption is one way -- it fills an empty owner and never
/// moves a set one -- because the slug is an address other identities resolve against.
/// </summary>
public class DriveOwningAppTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: [TestIdentities.Frodo]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }

    [Test]
    public async Task AdoptingAnUnownedDriveSetsTheAppAndDerivesASlug()
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var appId = Guid.NewGuid();
        var appResponse = await ownerApiClient.AppManager.RegisterApp(appId, new PermissionSetGrantRequest());
        ClassicAssert.IsTrue(appResponse.IsSuccessStatusCode);

        var drive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(drive, "Field Notes", "", false))
            .IsSuccessStatusCode);

        var before = await GetDrive(ownerApiClient, drive);
        Assert.That(before.AppId, Is.Null, "a drive created without an app must start unowned");
        Assert.That(before.DriveSlug, Is.Null, "AppId and DriveSlug are set together or both null");

        var response = await ownerApiClient.DriveManager.SetDriveOwningApp(drive, appId);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");

        var after = await GetDrive(ownerApiClient, drive);
        Assert.That(after.AppId, Is.EqualTo(appId));

        // The invariant that makes UNIQUE(identityId, AppId, DriveSlug) mean anything: an app-owned
        // row with no slug would sit outside the constraint entirely.
        Assert.That(after.DriveSlug, Is.Not.Null.And.Not.Empty);
        Assert.That(after.DriveTypeSlug, Is.Not.Null.And.Not.Empty);

        // Adoption is an addressing change; it must not touch what the drive is or holds.
        Assert.That(after.Name, Is.EqualTo(before.Name));
        Assert.That(after.AllowAnonymousReads, Is.EqualTo(before.AllowAnonymousReads));
        Assert.That(after.OwnerOnly, Is.EqualTo(before.OwnerOnly));
        Assert.That(after.IsArchived, Is.EqualTo(before.IsArchived));
    }

    [Test]
    public async Task ASuppliedSlugIsUsedVerbatim()
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var appId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(appId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var drive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(drive, "Anything", "", false))
            .IsSuccessStatusCode);

        var response = await ownerApiClient.DriveManager.SetDriveOwningApp(drive, appId, "news", "channel");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");

        var after = await GetDrive(ownerApiClient, drive);
        Assert.That(after.DriveSlug, Is.EqualTo("news"));
        Assert.That(after.DriveTypeSlug, Is.EqualTo("channel"));
    }

    [Test]
    public async Task ASlugAlreadyTakenByTheSameAppIsRefused()
    {
        // Refused rather than suffixed: a supplied slug is an address the caller intends to resolve
        // against, so handing back "news-2" would look like success.
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var appId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(appId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var first = TargetDrive.NewTargetDrive();
        var second = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(first, "First", "", false))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(second, "Second", "", false))
            .IsSuccessStatusCode);

        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.SetDriveOwningApp(first, appId, "news"))
            .IsSuccessStatusCode);

        var response = await ownerApiClient.DriveManager.SetDriveOwningApp(second, appId, "news");
        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var secondAfter = await GetDrive(ownerApiClient, second);
        Assert.That(secondAfter.AppId, Is.Null, "the refused call must leave the drive adoptable");
    }

    [Test]
    public async Task TheSameSlugUnderADifferentAppIsAllowed()
    {
        // The constraint is per app, not per identity: feed/news and chat/news may coexist.
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(firstAppId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(secondAppId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var first = TargetDrive.NewTargetDrive();
        var second = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(first, "First", "", false))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(second, "Second", "", false))
            .IsSuccessStatusCode);

        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.SetDriveOwningApp(first, firstAppId, "news"))
            .IsSuccessStatusCode);

        var response = await ownerApiClient.DriveManager.SetDriveOwningApp(second, secondAppId, "news");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");

        Assert.That((await GetDrive(ownerApiClient, second)).DriveSlug, Is.EqualTo("news"));
    }

    [Test]
    public async Task AdoptingADriveThatAlreadyHasAnAppIsRefused()
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(firstAppId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(secondAppId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var drive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(drive, "Adopted once", "", false))
            .IsSuccessStatusCode);

        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.SetDriveOwningApp(drive, firstAppId))
            .IsSuccessStatusCode);

        var response = await ownerApiClient.DriveManager.SetDriveOwningApp(drive, secondAppId);
        Assert.That(response.IsSuccessStatusCode, Is.False, "ownership must not be reassignable");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        Assert.That((await GetDrive(ownerApiClient, drive)).AppId, Is.EqualTo(firstAppId),
            "the refused call must not have moved it");
    }

    [Test]
    public async Task AdoptingASystemDriveIsRefused()
    {
        // Provisioned drives already belong to the app that ships them; the ones still carrying a
        // null AppId wait on provisioning to stamp it, not on the owner to guess.
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var appId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(appId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var systemDrive = BuiltinDrives.Protected.First();

        var response = await ownerApiClient.DriveManager.SetDriveOwningApp(systemDrive, appId);
        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AdoptingByAnUnregisteredAppIsRefused()
    {
        // Checked before the write: a mistyped id would strand the drive -- owned by nothing real,
        // and no longer adoptable.
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var drive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(drive, "Offered to nobody", "", false))
            .IsSuccessStatusCode);

        var response = await ownerApiClient.DriveManager.SetDriveOwningApp(drive, Guid.NewGuid());
        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        Assert.That((await GetDrive(ownerApiClient, drive)).AppId, Is.Null,
            "the drive must still be adoptable");
    }

    [Test]
    public async Task AnExistingSlugIsKeptRatherThanRegenerated()
    {
        // A drive can carry a slug with no AppId: CreateDriveAsync only skips *deriving* one for an
        // app-less drive, and a supplied one passes through. Adoption is when that slug starts
        // resolving, so it must not be swapped for a name-derived one on the way past.
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var appId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(appId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var drive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager
                .CreateDrive(drive, "Something Else Entirely", "", false, driveSlug: "news"))
            .IsSuccessStatusCode);

        var before = await GetDrive(ownerApiClient, drive);
        Assert.That(before.AppId, Is.Null);
        Assert.That(before.DriveSlug, Is.EqualTo("news"), "precondition: the drive carries a slug already");

        // No slug supplied -- the pre-existing one must survive rather than being derived from
        // "Something Else Entirely".
        var response = await ownerApiClient.DriveManager.SetDriveOwningApp(drive, appId);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");

        var after = await GetDrive(ownerApiClient, drive);
        Assert.That(after.AppId, Is.EqualTo(appId));
        Assert.That(after.DriveSlug, Is.EqualTo("news"), "adoption must not rewrite an existing slug");
    }

    [Test]
    public async Task AdoptingWithTheSlugItAlreadyHasIsAllowed()
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var appId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(appId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var drive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager
                .CreateDrive(drive, "Whatever", "", false, driveSlug: "news"))
            .IsSuccessStatusCode);

        var response = await ownerApiClient.DriveManager.SetDriveOwningApp(drive, appId, "news");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");
        Assert.That((await GetDrive(ownerApiClient, drive)).DriveSlug, Is.EqualTo("news"));
    }

    [Test]
    public async Task AdoptingWithADifferentSlugThanTheDriveCarriesIsRefused()
    {
        // Two explicit answers that disagree. Refused rather than picking one, so renaming an
        // address is never something adoption does on the way past.
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var appId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(appId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var drive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager
                .CreateDrive(drive, "Whatever", "", false, driveSlug: "news"))
            .IsSuccessStatusCode);

        var response = await ownerApiClient.DriveManager.SetDriveOwningApp(drive, appId, "headlines");
        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var after = await GetDrive(ownerApiClient, drive);
        Assert.That(after.AppId, Is.Null, "the refused call must leave the drive adoptable");
        Assert.That(after.DriveSlug, Is.EqualTo("news"), "and must leave its slug alone");
    }

    [Test]
    public async Task AKeptSlugThatCollidesWithinTheTargetAppIsRefused()
    {
        // The slug was unconstrained while the drive had no AppId, so it may well collide with one
        // the target app already holds. Reaching the insert would surface that as a raw UNIQUE
        // violation rather than a client error.
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var appId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(appId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var owned = TargetDrive.NewTargetDrive();
        var orphan = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(owned, "Owned", "", false))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager
                .CreateDrive(orphan, "Orphan", "", false, driveSlug: "news"))
            .IsSuccessStatusCode);

        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.SetDriveOwningApp(owned, appId, "news"))
            .IsSuccessStatusCode);

        // The orphan keeps "news", which the target app now holds.
        var response = await ownerApiClient.DriveManager.SetDriveOwningApp(orphan, appId);
        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AMalformedSlugIsRefused()
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var appId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(appId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var drive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(drive, "Anything", "", false))
            .IsSuccessStatusCode);

        // Not coerced to "news": a slug is a wire address, so a malformed one is rejected rather
        // than turned into an address the caller did not ask for.
        var response = await ownerApiClient.DriveManager.SetDriveOwningApp(drive, appId, " News ");
        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    //
    // Reassignment: the escape hatch out of the one-way rule.
    //

    [Test]
    public async Task ReassigningMovesTheDriveAndItsAddress()
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(firstAppId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(secondAppId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var drive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(drive, "Movable", "", false))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.SetDriveOwningApp(drive, firstAppId, "news"))
            .IsSuccessStatusCode);

        var response = await ownerApiClient.DriveManager
            .ReassignDriveOwningApp(drive, secondAppId, "headlines");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");

        var after = await GetDrive(ownerApiClient, drive);
        Assert.That(after.AppId, Is.EqualTo(secondAppId));
        Assert.That(after.DriveSlug, Is.EqualTo("headlines"));
    }

    [Test]
    public async Task ReassigningWithoutASlugIsRefused()
    {
        // Required rather than derived: the address changes, so it is stated rather than discovered.
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(firstAppId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(secondAppId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var drive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(drive, "Movable", "", false))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.SetDriveOwningApp(drive, firstAppId, "news"))
            .IsSuccessStatusCode);

        var response = await ownerApiClient.DriveManager.ReassignDriveOwningApp(drive, secondAppId);
        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var after = await GetDrive(ownerApiClient, drive);
        Assert.That(after.AppId, Is.EqualTo(firstAppId), "the refused call must not have moved it");
    }

    [Test]
    public async Task ReassigningOntoASlugTheNewAppHoldsIsRefused()
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(firstAppId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(secondAppId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var occupant = TargetDrive.NewTargetDrive();
        var mover = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(occupant, "Occupant", "", false))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(mover, "Mover", "", false))
            .IsSuccessStatusCode);

        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.SetDriveOwningApp(occupant, secondAppId, "news"))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.SetDriveOwningApp(mover, firstAppId, "mover"))
            .IsSuccessStatusCode);

        var response = await ownerApiClient.DriveManager.ReassignDriveOwningApp(mover, secondAppId, "news");
        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ReassigningKeepingItsOwnSlugIsAllowed()
    {
        // The drive's own row must not count as an occupant of the slug it already holds.
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(firstAppId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(secondAppId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var drive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.CreateDrive(drive, "Movable", "", false))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await ownerApiClient.DriveManager.SetDriveOwningApp(drive, firstAppId, "news"))
            .IsSuccessStatusCode);

        var response = await ownerApiClient.DriveManager.ReassignDriveOwningApp(drive, secondAppId, "news");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");

        var after = await GetDrive(ownerApiClient, drive);
        Assert.That(after.AppId, Is.EqualTo(secondAppId));
        Assert.That(after.DriveSlug, Is.EqualTo("news"));
    }

    [Test]
    public async Task ReassigningASystemDriveIsRefused()
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var appId = Guid.NewGuid();
        ClassicAssert.IsTrue((await ownerApiClient.AppManager.RegisterApp(appId, new PermissionSetGrantRequest()))
            .IsSuccessStatusCode);

        var response = await ownerApiClient.DriveManager
            .ReassignDriveOwningApp(BuiltinDrives.Protected.First(), appId, "anything");
        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private static async Task<OwnerClientDriveData> GetDrive(OwnerApiClientRedux client, TargetDrive drive)
    {
        var response = await client.DriveManager.GetDrives(1, 1000);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        return response.Content!.Results.Single(d => d.TargetDriveInfo == drive);
    }
}
