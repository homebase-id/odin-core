using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Base;
using Odin.Services.Drives;
using Refit;

namespace Odin.Hosting.Tests.V2.Addressing;

/// <summary>
/// The local slug-addressed drive routes, <c>/api/v2/apps/{appSlug}/drives[/{driveSlug}]</c>
/// (docs/drive-addressing.md).  These name a drive by app and slug instead of by guid.
/// </summary>
/// <remarks>
/// The app half of the address is a real registration -- these tests use a built-in app the tree
/// provisions, so the slug resolves the same way it will in production rather than through anything
/// the test invented.
/// </remarks>
[TestFixture]
public class AppDriveAddressingTests : V2Fixture
{
    // Chat is built-in: BuiltinApps registers it with slug "chat" and BuiltinDrives gives it the
    // "chat" and "stickers" drives.  Provisioned before any test runs.
    private const string ChatAppSlug = "chat";
    private const string ChatDriveSlug = "chat";
    private const string StickerDriveSlug = "stickers";

    private static IAppDriveHttpClientApiV2 ClientFor(OwnerSession owner)
    {
        var (client, ss) = owner.NewAdminHttpClient();
        return RefitCreator.RestServiceFor<IAppDriveHttpClientApiV2>(client, ss);
    }

    [Test]
    public async Task ListsTheDrivesAnAppOwns()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var response = await ClientFor(owner).GetAppDrives(ChatAppSlug);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var slugs = response.Content!.Select(d => d.DriveSlug).ToList();
        Assert.That(slugs, Does.Contain(ChatDriveSlug));
        Assert.That(slugs, Does.Contain(StickerDriveSlug));

        // Every drive the route returns must actually belong to the app it was asked about --
        // otherwise the listing is just "all drives" wearing an app's name.
        Assert.That(response.Content.All(d => d.AppId.HasValue), Is.True);
        Assert.That(response.Content.Select(d => d.AppId!.Value).Distinct().Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task ListingCarriesTheGuidAddressSoOneCallIsEnoughToUseTheRestOfTheApi()
    {
        // The point of the endpoint: you arrive knowing a name and leave holding the TargetDrive
        // every other route takes.
        var owner = await LoginAsOwner(Identities.Frodo);
        var response = await ClientFor(owner).GetAppDrives(ChatAppSlug);

        var chat = response.Content!.Single(d => d.DriveSlug == ChatDriveSlug);
        Assert.That(chat.TargetDrive, Is.Not.Null);
        Assert.That(chat.TargetDrive.Alias.Value, Is.Not.EqualTo(Guid.Empty));
        Assert.That(chat.TargetDrive.Type.Value, Is.Not.EqualTo(Guid.Empty));
        Assert.That(chat.Name, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task FiltersByDriveTypeSlug()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var all = await ClientFor(owner).GetAppDrives(ChatAppSlug);
        var filtered = await ClientFor(owner).GetAppDrives(ChatAppSlug, type: "sticker");

        Assert.That(filtered.IsSuccessStatusCode, Is.True, $"got {filtered.StatusCode}");
        Assert.That(filtered.Content!.Count, Is.LessThan(all.Content!.Count),
            "the filter has to actually narrow, or it is not filtering");
        Assert.That(filtered.Content.All(d => d.DriveTypeSlug == "sticker"), Is.True);
        Assert.That(filtered.Content.Select(d => d.DriveSlug), Does.Contain(StickerDriveSlug));
    }

    [Test]
    public async Task UnknownTypeSlugReturnsEmptyRatherThanEverything()
    {
        // A filter nobody matches must return nothing, not fall back to unfiltered -- the failure
        // mode here is silently over-returning.
        var owner = await LoginAsOwner(Identities.Frodo);
        var response = await ClientFor(owner).GetAppDrives(ChatAppSlug, type: "no-such-type");

        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(response.Content, Is.Empty);
    }

    [Test]
    public async Task ResolvesASingleDriveBySlug()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var response = await ClientFor(owner).GetAppDrive(ChatAppSlug, ChatDriveSlug);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That(response.Content!.DriveSlug, Is.EqualTo(ChatDriveSlug));
        Assert.That(response.Content.DriveTypeSlug, Is.EqualTo("chat"));
        Assert.That(response.Content.TargetDrive.Alias.Value, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task SingleDriveAgreesWithTheListing()
    {
        // Two routes, one drive: if they disagree, one of them is lying about the address.
        var owner = await LoginAsOwner(Identities.Frodo);

        var fromList = (await ClientFor(owner).GetAppDrives(ChatAppSlug)).Content!
            .Single(d => d.DriveSlug == ChatDriveSlug);
        var direct = (await ClientFor(owner).GetAppDrive(ChatAppSlug, ChatDriveSlug)).Content!;

        Assert.That(direct.TargetDrive, Is.EqualTo(fromList.TargetDrive));
        Assert.That(direct.Name, Is.EqualTo(fromList.Name));
        Assert.That(direct.AppId, Is.EqualTo(fromList.AppId));
    }

    [Test]
    public async Task UnknownAppSlugIsRejected()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var response = await ClientFor(owner).GetAppDrives("no-such-app");

        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task UnknownDriveSlugIsRejected()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var response = await ClientFor(owner).GetAppDrive(ChatAppSlug, "no-such-drive");

        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task UnknownAppAndUnknownDriveAreIndistinguishable()
    {
        // Deliberate: telling the two apart would let a caller enumerate an identity's apps and
        // drives by name.  Asserted so the property is not lost in a later refactor.
        var owner = await LoginAsOwner(Identities.Frodo);

        var badApp = await ClientFor(owner).GetAppDrive("no-such-app", ChatDriveSlug);
        var badDrive = await ClientFor(owner).GetAppDrive(ChatAppSlug, "no-such-drive");

        Assert.That(badApp.StatusCode, Is.EqualTo(badDrive.StatusCode));
    }

    private static Task RegisterAppAsync(OwnerSession owner, Guid appId, string appSlug) =>
        owner.Admin.RegisterApp(appId, new PermissionSetGrantRequest(), appSlug: appSlug);

    [Test]
    public async Task ADriveCreatedWithAnExplicitSlugIsAddressableByIt()
    {
        // The round trip that matters for a client creating its own drive: name it, then reach it.
        var owner = await LoginAsOwner(Identities.Frodo);

        var appId = Guid.NewGuid();
        var appSlug = $"t{Guid.NewGuid():N}"[..12];
        await RegisterAppAsync(owner, appId, appSlug);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Notes", appId: appId, driveSlug: "notes",
            driveTypeSlug: "note");

        var response = await ClientFor(owner).GetAppDrive(appSlug, "notes");

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That(response.Content!.TargetDrive, Is.EqualTo(drive));
        Assert.That(response.Content.DriveTypeSlug, Is.EqualTo("note"));
    }

    [Test]
    public async Task SlugsAreUniquePerAppNotPerIdentity()
    {
        // feed/news and chat/news are different drives.  This is the reason resolution needs both
        // halves, so it is worth pinning.
        var owner = await LoginAsOwner(Identities.Frodo);

        var appA = Guid.NewGuid();
        var appB = Guid.NewGuid();
        var slugA = $"a{Guid.NewGuid():N}"[..12];
        var slugB = $"b{Guid.NewGuid():N}"[..12];
        await RegisterAppAsync(owner, appA, slugA);
        await RegisterAppAsync(owner, appB, slugB);

        var driveA = TargetDrive.NewTargetDrive();
        var driveB = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(driveA, "News A", appId: appA, driveSlug: "news");
        await owner.Admin.CreateDrive(driveB, "News B", appId: appB, driveSlug: "news");

        var fromA = await ClientFor(owner).GetAppDrive(slugA, "news");
        var fromB = await ClientFor(owner).GetAppDrive(slugB, "news");

        Assert.That(fromA.Content!.TargetDrive, Is.EqualTo(driveA));
        Assert.That(fromB.Content!.TargetDrive, Is.EqualTo(driveB));
        Assert.That(fromA.Content.TargetDrive, Is.Not.EqualTo(fromB.Content.TargetDrive));
    }

    [Test]
    public async Task ASecondDriveCannotClaimASlugTheAppAlreadyHolds()
    {
        // Refused rather than suffixed: a supplied slug is an address, so handing back a different
        // one would look like success.
        var owner = await LoginAsOwner(Identities.Frodo);

        var appId = Guid.NewGuid();
        var appSlug = $"c{Guid.NewGuid():N}"[..12];
        await RegisterAppAsync(owner, appId, appSlug);

        await owner.Admin.CreateDrive(TargetDrive.NewTargetDrive(), "First", appId: appId,
            driveSlug: "shared");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await owner.Admin.CreateDrive(TargetDrive.NewTargetDrive(), "Second", appId: appId,
                driveSlug: "shared"));

        // The error has to name the conflict and carry idAlreadyExists -- a client needs to tell a
        // taken slug apart from a malformed one, and a raw UNIQUE violation would say neither.
        Assert.That(ex!.Message, Does.Contain("400"));
        Assert.That(ex.Message, Does.Contain("idAlreadyExists"));
        Assert.That(ex.Message, Does.Contain("shared"));
    }

    [Test]
    public async Task ADriveWithAnAppButNoSlugGetsOneDerivedFromItsName()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var appId = Guid.NewGuid();
        var appSlug = $"d{Guid.NewGuid():N}"[..12];
        await RegisterAppAsync(owner, appId, appSlug);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Weekly Notes", appId: appId);

        var listed = (await ClientFor(owner).GetAppDrives(appSlug)).Content!
            .Single(d => d.TargetDrive == drive);

        Assert.That(listed.DriveSlug, Is.Not.Null.And.Not.Empty,
            "a drive with an owning app must end up addressable, derived or not");
    }
}
