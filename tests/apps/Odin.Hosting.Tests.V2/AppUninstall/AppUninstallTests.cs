using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Services.Apps;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Refit;

namespace Odin.Hosting.Tests.V2.AppUninstall;

public interface IRefitAppUninstall
{
    [Post(OwnerApiPathConstants.AppManagementV1 + "/uninstall")]
    Task<ApiResponse<HttpContent>> Uninstall([Body] UninstallAppRequest request);

    [Post(OwnerApiPathConstants.AppManagementV1 + "/register/updateauthorizedcircles")]
    Task<ApiResponse<HttpContent>> UpdateAuthorizedCircles([Body] UpdateAuthorizedCirclesRequest request);
}

/// <summary>
/// Uninstalling a third-party app (#1870): built-in apps are refused, an app owning circles or drives is
/// refused unless they go with it, and nothing of the app is left behind -- clients, connection grants,
/// owned circles and drives, other apps' references to its circles.
/// </summary>
[TestFixture]
public class AppUninstallTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task ABuiltInAppIsRefused()
    {
        var owner = await LoginAsOwner();

        Assert.That(await UninstallAsync(owner, SystemAppConstants.ChatAppId), Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AnAppWithNothingOwnedIsRemovedWithItsClients()
    {
        var owner = await LoginAsOwner();
        var appId = await owner.Admin.RegisterBareApp();
        await owner.Admin.RegisterAppClient(appId);
        Assert.That(await AppClientRowsAsync(owner, appId), Is.EqualTo(1), "arrange: no client row");

        Assert.That(await UninstallAsync(owner, appId), Is.EqualTo(HttpStatusCode.OK));

        Assert.That(await IsRegisteredAsync(owner, appId), Is.False);
        Assert.That(await AppClientRowsAsync(owner, appId), Is.EqualTo(0), "a client outlived its app");
    }

    [Test]
    public async Task OwnedDrivesAreDeletedOnlyWhenAskedFor()
    {
        var owner = await LoginAsOwner();
        var appId = await owner.Admin.RegisterBareApp();
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "app drive", allowAnonymousReads: false, appId: appId);

        Assert.That(await UninstallAsync(owner, appId), Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await IsRegisteredAsync(owner, appId), Is.True);

        Assert.That(await UninstallAsync(owner, appId, deleteOwned: true), Is.EqualTo(HttpStatusCode.OK));

        Assert.That(await IsRegisteredAsync(owner, appId), Is.False);
        Assert.That((await owner.Admin.GetDrives()).Any(d => d.TargetDriveInfo == drive), Is.False, "the app's drive outlived it");
    }

    [Test]
    public async Task OwnedCirclesGoAndNoConnectionOrAppKeepsAGrantFromTheApp()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await PeerFlow.ConnectAsync(frodo, sam);

        // The app owns a circle, authorizes it, and gives its members a grant; Sam is enrolled after review.
        var appId = Guid.NewGuid();
        await frodo.Admin.RegisterApp(appId, new());
        var circleId = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleId, "app circle", OwnerAdmin.ReadCircleMembershipGrant(), appId,
            CircleGrantOn.Review);
        await AuthorizeAsync(frodo, appId, circleId);

        // Another app authorizes the same circle.
        var otherAppId = await frodo.Admin.RegisterBareApp();
        await AuthorizeAsync(frodo, otherAppId, circleId);

        var review = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory).MarkReviewedAsync(sam.Identity);
        Assert.That(review.IsSuccessStatusCode, Is.True, "arrange: review failed");
        var enrollment = (await frodo.Admin.GrantCircleToMany(circleId, [sam.Identity])).Content!;
        Assert.That(enrollment.Enrolled, Is.EqualTo(1), "arrange: Sam was not enrolled");

        var before = (await frodo.Connections.GetConnectionInfo(sam.Identity)).Content!.AccessGrant;
        Assert.That(before.CircleGrants.Any(c => c.CircleId == circleId), Is.True, "arrange: Sam is not in the circle");
        Assert.That(before.AppGrants.ContainsKey(appId), Is.True, "arrange: Sam holds no grant from the app");

        Assert.That(await UninstallAsync(frodo, appId, deleteOwned: true), Is.EqualTo(HttpStatusCode.OK));

        var after = (await frodo.Connections.GetConnectionInfo(sam.Identity)).Content!.AccessGrant;
        Assert.That(after.CircleGrants.Any(c => c.CircleId == circleId), Is.False, "Sam is still in the app's circle");
        Assert.That(after.AppGrants.ContainsKey(appId), Is.False, "Sam still holds a grant from the app");

        var circles = (await frodo.RefitFor<IRefitOwnerCircleDefinition>().GetCircleDefinitions(includeSystemCircle: true)).Content!;
        Assert.That(circles.Any(c => c.Id == circleId), Is.False, "the app's circle outlived it");

        var other = (await frodo.RefitFor<IRefitOwnerAppRegistration>().GetRegisteredApp(new GetAppRequest { AppId = otherAppId })).Content!;
        Assert.That(other.AuthorizedCircles, Does.Not.Contain(circleId), "another app still authorizes the deleted circle");
    }

    private static async Task<HttpStatusCode> UninstallAsync(OwnerSession owner, Guid appId, bool deleteOwned = false) =>
        (await owner.RefitFor<IRefitAppUninstall>().Uninstall(new UninstallAppRequest
        {
            AppId = appId,
            DeleteOwnedCirclesAndDrives = deleteOwned
        })).StatusCode;

    private static async Task AuthorizeAsync(OwnerSession owner, Guid appId, Guid circleId)
    {
        var response = await owner.RefitFor<IRefitAppUninstall>().UpdateAuthorizedCircles(new UpdateAuthorizedCirclesRequest
        {
            AppId = appId,
            AuthorizedCircles = [circleId],
            CircleMemberPermissionGrant = OwnerAdmin.ReadCircleMembershipGrant()
        });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"authorize: {response.StatusCode} {response.Error?.Content}");
    }

    private static async Task<bool> IsRegisteredAsync(OwnerSession owner, Guid appId)
    {
        var response = await owner.RefitFor<IRefitOwnerAppRegistration>().GetRegisteredApp(new GetAppRequest { AppId = appId });
        return response.IsSuccessStatusCode && response.Content != null;
    }

    private async Task<int> AppClientRowsAsync(OwnerSession owner, Guid appId)
    {
        await using var scope = Host.GetTenantScope(owner.Identity.DomainName).BeginLifetimeScope();
        var rows = await scope.Resolve<IdentityDatabase>().ClientRegistrations
            .GetByTypeAndCategoryIdAsync(AppClientRegistration.CatType, appId);
        return rows.Count;
    }
}
