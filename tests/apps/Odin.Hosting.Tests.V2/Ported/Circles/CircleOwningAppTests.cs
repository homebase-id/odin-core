using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Odin.Hosting.Controllers.OwnerToken.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.V2.Ported.Circles;

/// <summary>
/// Port of <c>OwnerApi/Membership/Circles/CircleOwningAppTests</c>.
///
/// Giving a circle an owning app, and moving it once it has one.
/// </summary>
/// <remarks>
/// The V1 original was its own fixture rather than more cases on <c>CircleDefinitionTests</c>:
/// these create circles, that class asserts on how many circles the identity has, and NUnit ran
/// both against the same scaffold and identity.  That collision is gone here — each fixture boots
/// its own host and every test starts from a restored snapshot — but the split is kept, because the
/// two fixtures are about different things.
///
/// The original pinned <c>TestIdentities.Samwise</c>; nothing here reads the identity (no peer flow,
/// no cross-identity assertion), so this runs as the fixture default.
///
/// <c>SetupCallerWithOwner</c> is not used: every test here is owner-only and creates its own
/// circles, so the "drive create and caller build collapse into one step" hazard does not apply.
/// </remarks>
[TestFixture]
public class CircleOwningAppTests : V2Fixture
{
    //
    // Adoption: giving an unowned circle an owning app.
    //
    // A circle the owner made in the console belongs to the owner-console app, which is right for most
    // and wrong for the ones that were always meant to be an app's.  Adoption is one-way -- it takes a
    // circle from the owner console, never from another app -- because PendingEnrollment denormalises
    // AppId on the promise that ownership does not move between apps.
    //

    [Test]
    public async Task AdoptingAnOwnerConsoleCircleGivesItTheApp()
    {
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Circle awaiting an owner", OwnerAdmin.ReadCircleMembershipGrant());

        var before = await owner.Admin.GetCircleDefinition(circleId);
        Assert.That(before.AppId, Is.EqualTo(SystemAppConstants.OwnerConsoleAppId),
            "a circle created without an app belongs to the owner console");

        var adopt = await SetOwningApp(owner, circleId, appId);
        Assert.That(adopt.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {adopt.StatusCode}");

        var after = await owner.Admin.GetCircleDefinition(circleId);
        Assert.That(after.AppId, Is.EqualTo(appId));

        // Adoption names an administrator.  Everything the circle actually grants is untouched.
        Assert.That(after.Name, Is.EqualTo(before.Name));
        Assert.That(after.Description, Is.EqualTo(before.Description));
        Assert.That(after.Permissions.Keys, Does.Contain(PermissionKeys.ReadCircleMembership));
        Assert.That(after.GrantOn, Is.EqualTo(before.GrantOn));
        Assert.That(after.Designation, Is.EqualTo(before.Designation));
        Assert.That(after.Emoji, Is.EqualTo(before.Emoji));
        Assert.That(after.Disabled, Is.EqualTo(before.Disabled));
        Assert.That(after.Created, Is.EqualTo(before.Created));
    }

    [Test]
    public async Task AdoptingACircleThatAlreadyHasAnAppIsRefused()
    {
        var owner = await LoginAsOwner();

        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        await owner.Admin.RegisterApp(firstAppId, new PermissionSetGrantRequest());
        await owner.Admin.RegisterApp(secondAppId, new PermissionSetGrantRequest());

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Circle adopted once", OwnerAdmin.ReadCircleMembershipGrant());

        Assert.That((await SetOwningApp(owner, circleId, firstAppId)).IsSuccessStatusCode, Is.True);

        var second = await SetOwningApp(owner, circleId, secondAppId);

        Assert.That(second.IsSuccessStatusCode, Is.False, "ownership must not be reassignable");
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var after = await owner.Admin.GetCircleDefinition(circleId);
        Assert.That(after!.AppId, Is.EqualTo(firstAppId), "the refused call must not have moved it");
    }

    [Test]
    public async Task ReAdoptingByTheSameAppIsRefused()
    {
        // Not idempotent on purpose: a repeat is indistinguishable from two apps racing for the
        // circle, and that is the reading worth failing on.
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Circle adopted twice by one app", OwnerAdmin.ReadCircleMembershipGrant());

        Assert.That((await SetOwningApp(owner, circleId, appId)).IsSuccessStatusCode, Is.True);

        var again = await SetOwningApp(owner, circleId, appId);
        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AdoptingByAnUnregisteredAppIsRefused()
    {
        // Checked before the write: stamping an app that does not exist would leave the circle in
        // the very state adoption exists to escape -- owned by something that can never claim it,
        // and no longer adoptable.
        var owner = await LoginAsOwner();

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Circle offered to nobody", OwnerAdmin.ReadCircleMembershipGrant());

        var response = await SetOwningApp(owner, circleId, Guid.NewGuid());
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var after = await owner.Admin.GetCircleDefinition(circleId);
        Assert.That(after!.AppId, Is.EqualTo(SystemAppConstants.OwnerConsoleAppId),
            "the circle must still be the owner's, and so still adoptable");
    }

    [Test]
    public async Task AdoptingACircleThatDoesNotExistIsRefused()
    {
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var response = await SetOwningApp(owner, Guid.NewGuid(), appId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    //
    // Reassignment: the escape hatch out of the one-way rule above.
    //

    [Test]
    public async Task ReassigningMovesTheCircleToTheNewApp()
    {
        var owner = await LoginAsOwner();

        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        await owner.Admin.RegisterApp(firstAppId, new PermissionSetGrantRequest());
        await owner.Admin.RegisterApp(secondAppId, new PermissionSetGrantRequest());

        var circleId = Guid.NewGuid();
        const string circleName = "Circle that moves";
        await owner.Admin.CreateCircle(circleId, circleName, OwnerAdmin.ReadCircleMembershipGrant());

        Assert.That((await SetOwningApp(owner, circleId, firstAppId)).IsSuccessStatusCode, Is.True);

        var response = await ReassignOwningApp(owner, circleId, secondAppId);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);
        Assert.That(response.Content.EnrollmentsRepointed, Is.EqualTo(0),
            "no enrollments were queued against this circle");

        var after = await owner.Admin.GetCircleDefinition(circleId);
        Assert.That(after!.AppId, Is.EqualTo(secondAppId));

        // Reassignment moves ownership only; it must not disturb what the circle grants.
        Assert.That(after.Name, Is.EqualTo(circleName));
        Assert.That(after.Permissions.Keys, Does.Contain(PermissionKeys.ReadCircleMembership));
    }

    [Test]
    public async Task ReassigningAnUnownedCircleIsAllowed()
    {
        // Reassign is the escape hatch, not a stricter adopt: it does not care what the circle
        // came from, only that the destination is real and the circle is not a system one.
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Never owned", OwnerAdmin.ReadCircleMembershipGrant());

        var response = await ReassignOwningApp(owner, circleId, appId);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {response.StatusCode}");
        Assert.That((await owner.Admin.GetCircleDefinition(circleId))!.AppId, Is.EqualTo(appId));
    }

    [Test]
    public async Task ReassigningASystemCircleIsRefused()
    {
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var response = await ReassignOwningApp(
            owner, SystemCircleConstants.ConfirmedConnectionsCircleId.Value, appId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ReassigningToAnUnregisteredAppIsRefused()
    {
        var owner = await LoginAsOwner();

        var appId = await owner.Admin.RegisterBareApp();

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Going nowhere", OwnerAdmin.ReadCircleMembershipGrant());

        Assert.That((await SetOwningApp(owner, circleId, appId)).IsSuccessStatusCode, Is.True);

        var response = await ReassignOwningApp(owner, circleId, Guid.NewGuid());
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await owner.Admin.GetCircleDefinition(circleId))!.AppId, Is.EqualTo(appId),
            "the refused call must not have moved it");
    }

    /// <summary>
    /// The circle set-owner endpoint as the system under test. <c>owner.Admin</c> is arrange-only —
    /// its helpers throw on non-2xx — so the calls this fixture asserts refusals on go through the
    /// Refit interface directly, via <see cref="OwnerSession.RefitFor{T}"/>.
    /// </summary>
    private static Task<ApiResponse<bool>> SetOwningApp(OwnerSession owner, Guid circleId, Guid appId) =>
        owner.RefitFor<IRefitOwnerCircleDefinition>().SetCircleOwningApp(new SetCircleOwningAppRequest
        {
            CircleId = circleId,
            AppId = appId,
        });

    /// <summary>
    /// As <see cref="SetOwningApp"/>, for the reassign endpoint. <c>OwnerAdmin.ReassignCircleOwningApp</c>
    /// would serve the two allowed cases but not the refusals, and it discards the typed
    /// <c>ReassignCircleOwningAppResult</c> this fixture asserts <c>EnrollmentsRepointed</c> on.
    /// </summary>
    private static Task<ApiResponse<ReassignCircleOwningAppResult>> ReassignOwningApp(
        OwnerSession owner, Guid circleId, Guid appId) =>
        owner.RefitFor<IRefitOwnerCircleDefinition>().ReassignCircleOwningApp(new SetCircleOwningAppRequest
        {
            CircleId = circleId,
            AppId = appId,
        });
}
