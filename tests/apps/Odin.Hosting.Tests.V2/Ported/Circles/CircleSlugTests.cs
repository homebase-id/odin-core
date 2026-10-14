using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.OwnerToken.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Services.Apps;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.V2.Ported.Circles;

/// <summary>
/// Circle slugs (#1939): a circle's address within its owning app.  The fix for an app that makes its
/// circle once per device -- Filesync ended up owning two "Filesync" circles -- is that
/// <c>circles/create</c> with a slug returns the circle the app already holds under it.
/// </summary>
[TestFixture]
public class CircleSlugTests : V2Fixture
{
    [Test]
    public async Task CreatingWithTheSameSlugTwiceReturnsTheSameCircle()
    {
        var owner = await LoginAsOwner();
        var app = await CreateAppAsync(owner, Guid.NewGuid());

        var first = await CreateAppCircle(app, "Filesync", "filesync");
        var second = await CreateAppCircle(app, "Filesync", "filesync");

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(second.Content, Is.EqualTo(first.Content), "the second device must find the first one's circle");

        var circles = await owner.RefitFor<IRefitOwnerCircleDefinition>().GetCircleDefinitions();
        Assert.That(circles.Content.Count(c => c.AppId == app.AppId), Is.EqualTo(1));

        var circle = await owner.Admin.GetCircleDefinition(first.Content);
        Assert.That(circle.Slug, Is.EqualTo("filesync"));
    }

    [Test]
    public async Task TwoCreatesAtOnceWithOneSlugMakeOneCircle()
    {
        var owner = await LoginAsOwner();
        var app = await CreateAppAsync(owner, Guid.NewGuid());

        var results = await Task.WhenAll(
            CreateAppCircle(app, "Racing", "racing"),
            CreateAppCircle(app, "Racing", "racing"));

        Assert.That(results.Select(r => r.StatusCode), Is.All.EqualTo(HttpStatusCode.OK));
        Assert.That(results[1].Content, Is.EqualTo(results[0].Content));
    }

    [Test]
    public async Task TwoAppsMayHoldTheSameSlug()
    {
        var owner = await LoginAsOwner();
        var firstApp = await CreateAppAsync(owner, Guid.NewGuid());
        var secondApp = await CreateAppAsync(owner, Guid.NewGuid());

        var first = await CreateAppCircle(firstApp, "Sync", "sync");
        var second = await CreateAppCircle(secondApp, "Sync", "sync");

        Assert.That(second.Content, Is.Not.EqualTo(first.Content),
            "a slug is scoped to its app, so one app cannot reach another's circle by naming it");
    }

    [Test]
    public async Task WithoutASlugEveryCreateMakesANewCircleSluggedFromItsName()
    {
        var owner = await LoginAsOwner();
        var app = await CreateAppAsync(owner, Guid.NewGuid());

        var first = await CreateAppCircle(app, "Travel Blog", slug: null);
        var second = await CreateAppCircle(app, "Travel Blog", slug: null);

        Assert.That(second.Content, Is.Not.EqualTo(first.Content));
        Assert.That((await owner.Admin.GetCircleDefinition(first.Content)).Slug, Is.EqualTo("travel-blog"));
        Assert.That((await owner.Admin.GetCircleDefinition(second.Content)).Slug, Is.EqualTo("travel-blog-2"));
    }

    [Test]
    public async Task AMalformedSlugIsRefused()
    {
        var owner = await LoginAsOwner();
        var app = await CreateAppAsync(owner, Guid.NewGuid());

        var response = await CreateAppCircle(app, "Bad", "Not A Slug");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task TheOwnerCannotCreateASecondCircleWithATakenSlug()
    {
        var owner = await LoginAsOwner();
        var client = owner.RefitFor<IRefitOwnerCircleDefinition>();

        var first = await client.CreateCircleDefinition(OwnerCircle("Family", "family"));
        Assert.That(first.IsSuccessStatusCode, Is.True, $"arrange: {first.StatusCode}");

        // Unlike an app's create, the owner's is not idempotent: it names the id, and a second circle under
        // the same slug is a mistake to report, not a repeat to absorb.
        var second = await client.CreateCircleDefinition(OwnerCircle("Family again", "family"));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AnUpdateCannotChangeTheSlug()
    {
        var owner = await LoginAsOwner();
        var request = OwnerCircle("Work", "work");
        Assert.That((await owner.RefitFor<IRefitOwnerCircleDefinition>().CreateCircleDefinition(request)).IsSuccessStatusCode);

        var circle = await owner.Admin.GetCircleDefinition(request.Id);
        circle.Slug = "renamed";
        circle.Name = "Work, renamed";
        var update = await owner.Admin.TryUpdateCircleDefinition(circle);
        Assert.That(update.IsSuccessStatusCode, Is.True, $"arrange: {update.StatusCode}");

        var after = await owner.Admin.GetCircleDefinition(request.Id);
        Assert.That(after.Name, Is.EqualTo("Work, renamed"));
        Assert.That(after.Slug, Is.EqualTo("work"), "the slug is immutable once written");
    }

    [Test]
    public async Task ABuiltinCircleCarriesItsFixedSlug()
    {
        var owner = await LoginAsOwner();
        var chat = await owner.Admin.GetCircleDefinition(BuiltinCircles.ChatCircle.Id);
        Assert.That(chat.Slug, Is.EqualTo("chat"));
    }

    [Test]
    public async Task AdoptingIntoAnAppThatHoldsTheSlugIsRefused()
    {
        var owner = await LoginAsOwner();
        var appId = await owner.Admin.RegisterBareApp();
        var client = owner.RefitFor<IRefitOwnerCircleDefinition>();

        var adopted = OwnerCircle("Shared", "shared");
        Assert.That((await client.CreateCircleDefinition(adopted)).IsSuccessStatusCode);
        Assert.That((await SetOwningApp(owner, adopted.Id, appId)).IsSuccessStatusCode, Is.True, "arrange: first adoption");

        // The owner console no longer holds "shared", so a second circle can take it -- and then cannot
        // follow the first into the app.
        var second = OwnerCircle("Shared too", "shared");
        Assert.That((await client.CreateCircleDefinition(second)).IsSuccessStatusCode);

        var refused = await SetOwningApp(owner, second.Id, appId);
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await owner.Admin.GetCircleDefinition(second.Id)).AppId, Is.EqualTo(SystemAppConstants.OwnerConsoleAppId),
            "the refused call must not have moved it");
    }

    //

    private static CreateCircleRequest OwnerCircle(string name, string slug) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slug = slug,
        Permissions = new PermissionSet(PermissionKeys.ReadCircleMembership)
    };

    private static Task<Refit.ApiResponse<Guid>> CreateAppCircle(AppSession app, string name, string slug) =>
        app.RefitFor<IConnectionNetworkHttpClientApiV2>().CreateCircle(new CreateAppCircleRequest
        {
            Name = name,
            Slug = slug,
            Permissions = new PermissionSet(PermissionKeys.ReadConnections)
        });

    private static Task<Refit.ApiResponse<bool>> SetOwningApp(OwnerSession owner, Guid circleId, Guid appId) =>
        owner.RefitFor<IRefitOwnerCircleDefinition>().SetCircleOwningApp(new SetCircleOwningAppRequest
        {
            CircleId = circleId,
            AppId = appId,
        });

    private static async Task<AppSession> CreateAppAsync(OwnerSession owner, Guid appId)
    {
        var appDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(appDrive, "Some Drive 1", allowAnonymousReads: false);
        return await AppSession.SetupAsync(owner, appDrive, DrivePermission.All, [PermissionKeys.ReadConnections],
            knownAppId: appId);
    }
}
