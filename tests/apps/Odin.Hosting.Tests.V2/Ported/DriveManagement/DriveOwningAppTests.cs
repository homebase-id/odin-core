using System;
using System.Net.Http;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Apps.Builtin;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Hosting.Tests.V2.Ported.DriveManagement;

/// <summary>
/// Port of <c>OwnerApi/Drive/Management/DriveOwningAppTests</c>.
///
/// Adoption: giving a drive that belongs to no app an owning app, and the address that goes with it.
///
/// Every drive predating the addressing work carries a null AppId, which reads as "the owner's own"
/// and leaves it unaddressable by slug.  Adoption is one way -- it fills an empty owner and never
/// moves a set one -- because the slug is an address other identities resolve against.
/// </summary>
[TestFixture]
public class DriveOwningAppTests : V2Fixture
{
    [Test]
    public async Task AdoptingAnOwnerConsoleDriveSetsTheAppAndKeepsItAddressed()
    {
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Field Notes", allowAnonymousReads: false);

        var before = await owner.Admin.GetDrive(drive);
        Assert.That(before.AppId, Is.EqualTo(SystemAppConstants.OwnerConsoleAppId),
            "a drive created without an app belongs to the owner console");
        Assert.That(before.DriveSlug, Is.Not.Null.And.Not.Empty, "and is addressed from the start");

        var response = await SetOwningApp(owner, drive, appId);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");

        var after = await owner.Admin.GetDrive(drive);
        Assert.That(after.AppId, Is.EqualTo(appId));

        // The invariant that makes UNIQUE(identityId, AppId, DriveSlug) mean anything: an app-owned
        // row with no slug would sit outside the constraint entirely.
        Assert.That(after.DriveSlug, Is.Not.Null.And.Not.Empty);

        // A drive of a type nothing recognises still gets a readable category: "drive".
        Assert.That(after.DriveTypeSlug, Is.EqualTo(DriveSlugGenerator.DefaultTypeSlug));

        // Adoption is an addressing change; it must not touch what the drive is or holds.
        Assert.That(after.Name, Is.EqualTo(before.Name));
        Assert.That(after.AllowAnonymousReads, Is.EqualTo(before.AllowAnonymousReads));
        Assert.That(after.OwnerOnly, Is.EqualTo(before.OwnerOnly));
        Assert.That(after.IsArchived, Is.EqualTo(before.IsArchived));
    }

    [Test]
    public async Task ASuppliedSlugIsUsedVerbatim()
    {
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Anything", allowAnonymousReads: false);

        var response = await SetOwningApp(owner, drive, appId, "news", "channel");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");

        var after = await owner.Admin.GetDrive(drive);
        Assert.That(after.DriveSlug, Is.EqualTo("news"));
        Assert.That(after.DriveTypeSlug, Is.EqualTo("channel"));
    }

    [Test]
    public async Task ASlugAlreadyTakenByTheSameAppIsRefused()
    {
        // Refused rather than suffixed: a supplied slug is an address the caller intends to resolve
        // against, so handing back "news-2" would look like success.
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var first = TargetDrive.NewTargetDrive();
        var second = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(first, "First", allowAnonymousReads: false);
        await owner.Admin.CreateDrive(second, "Second", allowAnonymousReads: false);

        Assert.That((await SetOwningApp(owner, first, appId, "news")).IsSuccessStatusCode, Is.True);

        var response = await SetOwningApp(owner, second, appId, "news");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var secondAfter = await owner.Admin.GetDrive(second);
        Assert.That(secondAfter.AppId, Is.EqualTo(SystemAppConstants.OwnerConsoleAppId),
            "the refused call must leave the drive with the owner, and so still adoptable");
    }

    [Test]
    public async Task TheSameSlugUnderADifferentAppIsAllowed()
    {
        // The constraint is per app, not per identity: feed/news and chat/news may coexist.
        var owner = await LoginAsOwner();

        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        await owner.Admin.RegisterApp(firstAppId, new PermissionSetGrantRequest());
        await owner.Admin.RegisterApp(secondAppId, new PermissionSetGrantRequest());

        var first = TargetDrive.NewTargetDrive();
        var second = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(first, "First", allowAnonymousReads: false);
        await owner.Admin.CreateDrive(second, "Second", allowAnonymousReads: false);

        Assert.That((await SetOwningApp(owner, first, firstAppId, "news")).IsSuccessStatusCode, Is.True);

        var response = await SetOwningApp(owner, second, secondAppId, "news");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");

        Assert.That((await owner.Admin.GetDrive(second)).DriveSlug, Is.EqualTo("news"));
    }

    [Test]
    public async Task AdoptingADriveThatAlreadyHasAnAppIsRefused()
    {
        var owner = await LoginAsOwner();

        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        await owner.Admin.RegisterApp(firstAppId, new PermissionSetGrantRequest());
        await owner.Admin.RegisterApp(secondAppId, new PermissionSetGrantRequest());

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Adopted once", allowAnonymousReads: false);

        Assert.That((await SetOwningApp(owner, drive, firstAppId)).IsSuccessStatusCode, Is.True);

        var response = await SetOwningApp(owner, drive, secondAppId);
        Assert.That(response.IsSuccessStatusCode, Is.False, "ownership must not be reassignable");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        Assert.That((await owner.Admin.GetDrive(drive)).AppId, Is.EqualTo(firstAppId),
            "the refused call must not have moved it");
    }

    [Test]
    public async Task AdoptingASystemDriveIsRefused()
    {
        // Provisioned drives already belong to the app that ships them; the ones still carrying a
        // null AppId wait on provisioning to stamp it, not on the owner to guess.
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var systemDrive = BuiltinDrives.Protected.First();

        var response = await SetOwningApp(owner, systemDrive, appId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AdoptingByAnUnregisteredAppIsRefused()
    {
        // Checked before the write: a mistyped id would strand the drive -- owned by nothing real,
        // and no longer adoptable.
        var owner = await LoginAsOwner();

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Offered to nobody", allowAnonymousReads: false);

        var response = await SetOwningApp(owner, drive, Guid.NewGuid());
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        Assert.That((await owner.Admin.GetDrive(drive)).AppId, Is.EqualTo(SystemAppConstants.OwnerConsoleAppId),
            "the drive must still be the owner's, and so still adoptable");
    }

    [Test]
    public async Task AnExistingSlugIsKeptRatherThanRegenerated()
    {
        // A drive can carry a slug with no AppId: CreateDriveAsync only skips *deriving* one for an
        // app-less drive, and a supplied one passes through. Adoption is when that slug starts
        // resolving, so it must not be swapped for a name-derived one on the way past.
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Something Else Entirely", allowAnonymousReads: false, driveSlug: "news");

        var before = await owner.Admin.GetDrive(drive);
        Assert.That(before.AppId, Is.EqualTo(SystemAppConstants.OwnerConsoleAppId));
        Assert.That(before.DriveSlug, Is.EqualTo("news"), "precondition: the drive carries a slug already");

        // No slug supplied -- the pre-existing one must survive rather than being derived from
        // "Something Else Entirely".
        var response = await SetOwningApp(owner, drive, appId);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");

        var after = await owner.Admin.GetDrive(drive);
        Assert.That(after.AppId, Is.EqualTo(appId));
        Assert.That(after.DriveSlug, Is.EqualTo("news"), "adoption must not rewrite an existing slug");
    }

    [Test]
    public async Task AdoptingWithTheSlugItAlreadyHasIsAllowed()
    {
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Whatever", allowAnonymousReads: false, driveSlug: "news");

        var response = await SetOwningApp(owner, drive, appId, "news");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");
        Assert.That((await owner.Admin.GetDrive(drive)).DriveSlug, Is.EqualTo("news"));
    }

    [Test]
    public async Task AdoptingWithADifferentSlugTakesTheSuppliedOne()
    {
        // The slug the drive carries is an owner-console address, and adoption changes the app half
        // anyway -- so nothing the caller can reach today is being renamed underneath them, and the
        // app taking the drive gets to say what it is called.
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Whatever", allowAnonymousReads: false, driveSlug: "news");

        var response = await SetOwningApp(owner, drive, appId, "headlines");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");

        var after = await owner.Admin.GetDrive(drive);
        Assert.That(after.AppId, Is.EqualTo(appId));
        Assert.That(after.DriveSlug, Is.EqualTo("headlines"));
    }

    [Test]
    public async Task AKeptSlugThatCollidesWithinTheTargetAppIsRefused()
    {
        // The slug was unconstrained while the drive had no AppId, so it may well collide with one
        // the target app already holds. Reaching the insert would surface that as a raw UNIQUE
        // violation rather than a client error.
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var owned = TargetDrive.NewTargetDrive();
        var orphan = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(owned, "Owned", allowAnonymousReads: false);
        await owner.Admin.CreateDrive(orphan, "Orphan", allowAnonymousReads: false, driveSlug: "news");

        Assert.That((await SetOwningApp(owner, owned, appId, "news")).IsSuccessStatusCode, Is.True);

        // The orphan keeps "news", which the target app now holds.
        var response = await SetOwningApp(owner, orphan, appId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AMalformedSlugIsRefused()
    {
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Anything", allowAnonymousReads: false);

        // Not coerced to "news": a slug is a wire address, so a malformed one is rejected rather
        // than turned into an address the caller did not ask for.
        var response = await SetOwningApp(owner, drive, appId, " News ");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    //
    // Reassignment: the escape hatch out of the one-way rule.
    //

    [Test]
    public async Task ReassigningMovesTheDriveAndItsAddress()
    {
        var owner = await LoginAsOwner();

        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        await owner.Admin.RegisterApp(firstAppId, new PermissionSetGrantRequest());
        await owner.Admin.RegisterApp(secondAppId, new PermissionSetGrantRequest());

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Movable", allowAnonymousReads: false);
        Assert.That((await SetOwningApp(owner, drive, firstAppId, "news")).IsSuccessStatusCode, Is.True);

        var response = await ReassignOwningApp(owner, drive, secondAppId, "headlines");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");

        var after = await owner.Admin.GetDrive(drive);
        Assert.That(after.AppId, Is.EqualTo(secondAppId));
        Assert.That(after.DriveSlug, Is.EqualTo("headlines"));
    }

    [Test]
    public async Task ReassigningWithoutASlugIsRefused()
    {
        // Required rather than derived: the address changes, so it is stated rather than discovered.
        var owner = await LoginAsOwner();

        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        await owner.Admin.RegisterApp(firstAppId, new PermissionSetGrantRequest());
        await owner.Admin.RegisterApp(secondAppId, new PermissionSetGrantRequest());

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Movable", allowAnonymousReads: false);
        Assert.That((await SetOwningApp(owner, drive, firstAppId, "news")).IsSuccessStatusCode, Is.True);

        var response = await ReassignOwningApp(owner, drive, secondAppId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var after = await owner.Admin.GetDrive(drive);
        Assert.That(after.AppId, Is.EqualTo(firstAppId), "the refused call must not have moved it");
    }

    [Test]
    public async Task ReassigningOntoASlugTheNewAppHoldsIsRefused()
    {
        var owner = await LoginAsOwner();

        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        await owner.Admin.RegisterApp(firstAppId, new PermissionSetGrantRequest());
        await owner.Admin.RegisterApp(secondAppId, new PermissionSetGrantRequest());

        var occupant = TargetDrive.NewTargetDrive();
        var mover = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(occupant, "Occupant", allowAnonymousReads: false);
        await owner.Admin.CreateDrive(mover, "Mover", allowAnonymousReads: false);

        Assert.That((await SetOwningApp(owner, occupant, secondAppId, "news")).IsSuccessStatusCode,
            Is.True);
        Assert.That((await SetOwningApp(owner, mover, firstAppId, "mover")).IsSuccessStatusCode,
            Is.True);

        var response = await ReassignOwningApp(owner, mover, secondAppId, "news");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ReassigningKeepingItsOwnSlugIsAllowed()
    {
        // The drive's own row must not count as an occupant of the slug it already holds.
        var owner = await LoginAsOwner();

        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        await owner.Admin.RegisterApp(firstAppId, new PermissionSetGrantRequest());
        await owner.Admin.RegisterApp(secondAppId, new PermissionSetGrantRequest());

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Movable", allowAnonymousReads: false);
        Assert.That((await SetOwningApp(owner, drive, firstAppId, "news")).IsSuccessStatusCode, Is.True);

        var response = await ReassignOwningApp(owner, drive, secondAppId, "news");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");

        var after = await owner.Admin.GetDrive(drive);
        Assert.That(after.AppId, Is.EqualTo(secondAppId));
        Assert.That(after.DriveSlug, Is.EqualTo("news"));
    }

    [Test]
    public async Task ReassigningASystemDriveIsRefused()
    {
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var response = await ReassignOwningApp(owner, BuiltinDrives.Protected.First(), appId, "anything");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>
    /// The drive-management endpoints as the system under test. <c>owner.Admin</c> is arrange-only —
    /// its helpers throw on non-2xx — so the calls this fixture asserts refusals on go through the
    /// Refit interface directly, via <see cref="OwnerSession.RefitFor{T}"/>.
    /// </summary>
    private static Task<ApiResponse<HttpContent>> SetOwningApp(
        OwnerSession owner, TargetDrive drive, Guid appId, string driveSlug = null, string driveTypeSlug = null) =>
        owner.RefitFor<IRefitDriveManagement>().SetDriveOwningApp(new SetDriveOwningAppRequest
        {
            TargetDrive = drive, AppId = appId, DriveSlug = driveSlug, DriveTypeSlug = driveTypeSlug,
        });

    /// <summary>As <see cref="SetOwningApp"/>, for the reassign endpoint.</summary>
    private static Task<ApiResponse<HttpContent>> ReassignOwningApp(
        OwnerSession owner, TargetDrive drive, Guid appId, string driveSlug = null, string driveTypeSlug = null) =>
        owner.RefitFor<IRefitDriveManagement>().ReassignDriveOwningApp(new SetDriveOwningAppRequest
        {
            TargetDrive = drive, AppId = appId, DriveSlug = driveSlug, DriveTypeSlug = driveTypeSlug,
        });

}
