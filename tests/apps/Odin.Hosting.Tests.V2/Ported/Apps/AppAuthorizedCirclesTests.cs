using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version19tov20;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Apps;

/// <summary>
/// An app may only be authorized for circles the identity has (#2001). The retired Confirmed Connections and Auto
/// Connections circles (#1809) are refused by name, since authorizing one silently grants nothing. A circle a
/// registration already holds is left alone, even once it is gone.
/// </summary>
[TestFixture]
public class AppAuthorizedCirclesTests : V2Fixture
{
    [Test]
    public async Task RegisteringAnAppForARetiredSystemCircleIsRefused()
    {
        var owner = await LoginAsOwner();

        var response = await RegisterAsync(owner, Guid.NewGuid(), [V19ToV20VersionMigrationService.RetiredSystemCircleIds[0]]);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Error?.Content, Does.Contain("retired"));
    }

    [Test]
    public async Task RegisteringAnAppForAnUnknownCircleIsRefused()
    {
        var owner = await LoginAsOwner();

        var response = await RegisterAsync(owner, Guid.NewGuid(), [Guid.NewGuid()]);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AnAppIsAuthorizedForACircleTheIdentityHas()
    {
        var owner = await LoginAsOwner();
        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Friends of the app", ACircleGrant());

        var response = await RegisterAsync(owner, Guid.NewGuid(), [circleId]);

        Assert.That(response.IsSuccessStatusCode, Is.True, response.Error?.Content);
        Assert.That(response.Content!.AuthorizedCircles, Is.EqualTo(new[] { circleId }));
    }

    [Test]
    public async Task AddingAnUnknownOrRetiredCircleToAnAppIsRefused()
    {
        var owner = await LoginAsOwner();
        var appId = Guid.NewGuid();
        Assert.That((await RegisterAsync(owner, appId, [])).IsSuccessStatusCode, Is.True);

        Assert.That(await UpdateStatusAsync(owner, appId, [Guid.NewGuid()]), Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await UpdateStatusAsync(owner, appId, [V19ToV20VersionMigrationService.RetiredSystemCircleIds[1]]),
            Is.EqualTo(HttpStatusCode.BadRequest));

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Readers", ACircleGrant());
        Assert.That(await UpdateStatusAsync(owner, appId, [circleId]), Is.Null, "a circle the identity has");
    }

    [Test]
    public async Task ACircleTheAppAlreadyHoldsIsNotCheckedAgain()
    {
        var owner = await LoginAsOwner();
        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Gone soon", ACircleGrant());
        var appId = Guid.NewGuid();
        Assert.That((await RegisterAsync(owner, appId, [circleId])).IsSuccessStatusCode, Is.True);

        var deleted = await owner.RefitFor<IRefitOwnerCircleDefinition>().DeleteCircleDefinition(circleId);
        Assume.That(deleted.IsSuccessStatusCode, $"the circle could not be deleted ({deleted.StatusCode}), so this case cannot be set up");

        // The registration still holds the deleted circle; resaving it as it is must not be refused
        Assert.That(await UpdateStatusAsync(owner, appId, [circleId]), Is.Null);
    }

    //

    // A circle must grant something
    private static PermissionSetGrantRequest ACircleGrant() => new()
    {
        PermissionSet = new PermissionSet(new List<int> { PermissionKeys.ReadConnections })
    };

    private static Task<ApiResponse<RedactedAppRegistration>> RegisterAsync(OwnerSession owner, Guid appId,
        List<Guid> authorizedCircles) =>
        owner.RefitFor<IRefitOwnerAppRegistration>().RegisterApp(new AppRegistrationRequest
        {
            AppId = appId,
            AppSlug = OwnerAdmin.SlugFor(appId),
            Name = $"Test_{appId}",
            AuthorizedCircles = authorizedCircles,
            CircleMemberPermissionGrant = new PermissionSetGrantRequest(),
        });

    // Null on success; the status otherwise (the Refit method returns a plain Task, which throws)
    private static async Task<HttpStatusCode?> UpdateStatusAsync(OwnerSession owner, Guid appId, List<Guid> authorizedCircles)
    {
        try
        {
            await owner.RefitFor<IRefitOwnerAppRegistration>().UpdateAuthorizedCircles(new UpdateAuthorizedCirclesRequest
            {
                AppId = appId,
                AuthorizedCircles = authorizedCircles,
                CircleMemberPermissionGrant = new PermissionSetGrantRequest(),
            });
            return null;
        }
        catch (ApiException e)
        {
            return e.StatusCode;
        }
    }
}
