using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.OwnerToken.Membership.Circles;
using Odin.Hosting.Tests.AppAPI.ApiClient.Membership.CircleMembership;
using Odin.Hosting.Tests.AppAPI.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.V2.Ported.Circles;

/// <summary>
/// Port of <c>AppAPI/Membership/AppCircleDefinitionTests</c>. What an app may do with circles: read
/// definitions and members when it holds <c>ReadCircleMembership</c>, and nothing at all otherwise —
/// no create, update, delete, enable or disable, and no peek at the system circle's members.
/// </summary>
/// <remarks>
/// The app caller is an <see cref="AppSession"/>; the two app-side Refit surfaces the V1
/// <c>AppApiClient</c> wrapped (<see cref="IAppCircleDefinitionClient"/> and
/// <see cref="ICircleMembershipAppHttpClient"/>) are reached directly through
/// <c>app.RefitFor&lt;T&gt;()</c>. Both declare absolute <c>/api/apps/v1/...</c> paths, so they need no
/// base-path rewriting.
/// <para>
/// <c>_scaffold.Scenarios.CreateConnectedHobbits</c> becomes <see cref="PeerFlow.ConnectAllAsync"/>.
/// The original read the acting identity's mesh circle off the <c>ScenarioContext</c> that helper
/// returned; <c>ConnectAllAsync</c> generates the circle ids internally and returns nothing, so
/// <see cref="MeshCircleIdAsync"/> reads it back instead, identified by the drive it grants — a drive
/// the test minted for the purpose, so exactly one circle can grant it. (Counting circles would not
/// do: a freshly initialised tenant already carries thirteen the app tree declares.) The original
/// also registered an
/// app per hobbit inside that helper; these three tests register their own app afterwards and never
/// touch the helper's, so that step is dropped (the same decision as
/// <c>Ported/Transit/HobbitScenario</c>).
/// </para>
/// <para>
/// Every hobbit test ended with <c>Scenarios.DisconnectHobbits()</c>, pure lifecycle restoration now
/// owned by per-test reset, so dropped.
/// </para>
/// <para>
/// The original pinned Frodo / Pippin / Merry per test purely because <c>WebScaffold</c> shared
/// identities process-wide. Only the three mesh tests need more than one identity, and none of the
/// assertions read which identity is acting, so the nine single-identity tests run as the fixture
/// default. <c>HostIdentities</c> still lists four because <see cref="PeerFlow.ConnectAllAsync"/>
/// resolves all four.
/// </para>
/// <para>
/// Carried, unchanged: <see cref="AppFailsGetCircleDefinitionListWithoutReadCircleMembershipPermission"/>
/// creates three circles it never looks at again — the app is refused before any list is built, so
/// the arrange is inert. Left as found.
/// </para>
/// <para>
/// No <c>SetupCallerWithOwner</c> is used (the app is built by <see cref="CreateAppAndClient"/> after
/// its drive exists, exactly as the original did), so its ordering caveat does not apply.
/// </para>
/// </remarks>
[TestFixture]
public class AppCircleDefinitionTests : V2Fixture
{
    protected override string[] HostIdentities =>
        [Identities.Frodo, Identities.Sam, Identities.Merry, Identities.Pippin];

    [Test]
    public async Task AppCannotSeeSystemCircleMembers()
    {
        var owner = await LoginAsOwner();
        var targetDrive = TargetDrive.NewTargetDrive();
        await ConnectHobbits(owner, targetDrive);

        var appClient = await CreateAppAndClient(owner, PermissionKeys.All.ToArray());

        var response = await GetDomainsInCircle(appClient, SystemCircleConstants.ConfirmedConnectionsCircleId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppCanGetCircleMembers()
    {
        var owner = await LoginAsOwner();
        var targetDrive = TargetDrive.NewTargetDrive();
        await ConnectHobbits(owner, targetDrive);
        var meshCircleId = await MeshCircleIdAsync(owner, targetDrive);

        var appClient = await CreateAppAndClient(owner, PermissionKeys.ReadCircleMembership);

        var response = await GetDomainsInCircle(appClient, meshCircleId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var list = response.Content;
        Assert.That(list, Is.Not.Null);
        Assert.That(list.Count(cdr => cdr.Domain.DomainName == Identities.Sam), Is.EqualTo(1));
        Assert.That(list.Count(cdr => cdr.Domain.DomainName == Identities.Merry), Is.EqualTo(1));
        Assert.That(list.Count(cdr => cdr.Domain.DomainName == Identities.Pippin), Is.EqualTo(1));
    }

    [Test]
    public async Task AppFailsToGetCircleMembersWithoutReadCircleMembershipPermission()
    {
        var owner = await LoginAsOwner();
        var targetDrive = TargetDrive.NewTargetDrive();
        await ConnectHobbits(owner, targetDrive);
        var meshCircleId = await MeshCircleIdAsync(owner, targetDrive);

        var appClient = await CreateAppAndClient(owner, PermissionKeys.ReadConnections);

        var response = await GetDomainsInCircle(appClient, meshCircleId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppCanGetCircleDefinition()
    {
        var owner = await LoginAsOwner();
        var def = await CreateRandomCircle(owner);

        var appClient = await CreateAppAndClient(owner, PermissionKeys.ReadCircleMembership);

        var getDefinitionResponse = await appClient.RefitFor<IAppCircleDefinitionClient>()
            .GetCircleDefinition(def.Id.Value);
        Assert.That(getDefinitionResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getDefinitionResponse.Content, Is.Not.Null);
        Assert.That(getDefinitionResponse.Content.Id, Is.EqualTo(def.Id));
        Assert.That(getDefinitionResponse.Content.Name, Is.EqualTo(def.Name));
    }

    [Test]
    public async Task AppFailsToGetCircleDefinitionWithoutReadCircleMembershipPermission()
    {
        var owner = await LoginAsOwner();
        var def = await CreateRandomCircle(owner);

        var appClient = await CreateAppAndClient(owner);

        var getDefinitionResponse = await appClient.RefitFor<IAppCircleDefinitionClient>()
            .GetCircleDefinition(def.Id.Value);
        Assert.That(getDefinitionResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppCanGetCircleDefinitionList()
    {
        var owner = await LoginAsOwner();

        // The circles belong to this app so the test holds whether or not the tenant has
        // HideOwnerCirclesFromApps on: with it on, an app is shown only app-owned circles
        // (CircleMembershipService.GetCircleDefinitions).
        var appId = Guid.NewGuid();
        var appClient = await CreateAppAndClient(owner, appId, PermissionKeys.ReadCircleMembership);

        var def1 = await CreateRandomCircle(owner, appId);
        var def2 = await CreateRandomCircle(owner, appId);
        var def3 = await CreateRandomCircle(owner, appId);

        var getDefinitionResponse = await appClient.RefitFor<IAppCircleDefinitionClient>().GetCircleDefinitions();
        Assert.That(getDefinitionResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getDefinitionResponse.Content, Is.Not.Null);
        Assert.That(getDefinitionResponse.Content.Count(d => d.Id == def1.Id), Is.EqualTo(1));
        Assert.That(getDefinitionResponse.Content.Count(d => d.Id == def2.Id), Is.EqualTo(1));
        Assert.That(getDefinitionResponse.Content.Count(d => d.Id == def3.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task AppFailsGetCircleDefinitionListWithoutReadCircleMembershipPermission()
    {
        var owner = await LoginAsOwner();

        var appClient = await CreateAppAndClient(owner);

        await CreateRandomCircle(owner);
        await CreateRandomCircle(owner);
        await CreateRandomCircle(owner);

        var getDefinitionResponse = await appClient.RefitFor<IAppCircleDefinitionClient>().GetCircleDefinitions();
        Assert.That(getDefinitionResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppFailsToUpdateCircleDefinition()
    {
        var owner = await LoginAsOwner();
        var appClient = await CreateAppAndClient(owner, PermissionKeys.ReadCircleMembership);
        var def1 = await CreateRandomCircle(owner);
        def1.Name = "another name";
        var response = await appClient.RefitFor<IAppCircleDefinitionClient>().UpdateCircleDefinition(def1);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppFailsToDeleteCircleDefinition()
    {
        var owner = await LoginAsOwner();
        var appClient = await CreateAppAndClient(owner, PermissionKeys.ReadCircleMembership);
        var def1 = await CreateRandomCircle(owner);
        var response = await appClient.RefitFor<IAppCircleDefinitionClient>().DeleteCircleDefinition(def1.Id.Value);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppFailsToDisableCircleDefinition()
    {
        var owner = await LoginAsOwner();
        var appClient = await CreateAppAndClient(owner, PermissionKeys.ReadCircleMembership);
        var def1 = await CreateRandomCircle(owner);
        var response = await appClient.RefitFor<IAppCircleDefinitionClient>().DisableCircleDefinition(def1.Id.Value);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppFailsToEnableCircleDefinition()
    {
        var owner = await LoginAsOwner();
        var appClient = await CreateAppAndClient(owner, PermissionKeys.ReadCircleMembership);
        var def1 = await CreateRandomCircle(owner);
        var response = await appClient.RefitFor<IAppCircleDefinitionClient>().EnableCircleDefinition(def1.Id.Value);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppFailsToCreateCircleDefinition()
    {
        var owner = await LoginAsOwner();
        var appClient = await CreateAppAndClient(owner, PermissionKeys.ReadCircleMembership);
        var response = await appClient.RefitFor<IAppCircleDefinitionClient>().CreateCircleDefinition(
            new CreateCircleRequest()
            {
                Id = Guid.NewGuid(),
                Name = "test",
                DriveGrants = default,
                Description = "test"
            });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    // -------------------------------------------------------------------------------------------
    // Arrange
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The four-identity mesh the original got from <c>Scenarios.CreateConnectedHobbits</c>, minus the
    /// per-hobbit app registration nothing here reads.
    /// </summary>
    private async Task ConnectHobbits(OwnerSession acting, TargetDrive targetDrive)
    {
        var others = new List<OwnerSession> { acting };
        foreach (var identity in HostIdentities.Where(i => i != acting.Identity.DomainName))
        {
            others.Add(await LoginAsOwner(identity));
        }

        await PeerFlow.ConnectAllAsync(others, targetDrive);
    }

    /// <summary>
    /// The circle <see cref="PeerFlow.ConnectAllAsync"/> created for this identity and granted to
    /// every peer, found by the one thing that distinguishes it: it is the circle that grants
    /// <paramref name="sharedDrive"/>, a drive the test minted seconds earlier. See the class remarks
    /// for why it has to be read back at all.
    /// </summary>
    private static async Task<Guid> MeshCircleIdAsync(OwnerSession owner, TargetDrive sharedDrive)
    {
        var response = await owner.RefitFor<IRefitOwnerCircleDefinition>().GetCircleDefinitions();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var matches = response.Content!
            .Where(d => d.DriveGrants != null &&
                        d.DriveGrants.Any(g => g.PermissionedDrive.Drive == sharedDrive))
            .ToList();

        Assert.That(matches.Count, Is.EqualTo(1),
            "exactly one circle should grant the drive the mesh connect was built over");
        return matches.Single().Id.Value;
    }

    private static Task<CircleDefinition> CreateRandomCircle(OwnerSession owner, params int[] permissionKeys)
    {
        return CreateRandomCircle(owner, null, permissionKeys);
    }

    /// <summary>
    /// Same, but handed to <paramref name="owningAppId"/> once created.  The create endpoint takes
    /// an AppId, but the owner-side helper this uses does not carry one, so the circle is adopted
    /// immediately afterwards instead.
    /// </summary>
    private static async Task<CircleDefinition> CreateRandomCircle(OwnerSession owner, Guid? owningAppId,
        params int[] permissionKeys)
    {
        var titleId = Guid.NewGuid();

        var appDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(appDrive, $"Drive for {titleId}", allowAnonymousReads: false);

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, $"Random circle {titleId}", new PermissionSetGrantRequest()
        {
            Drives = new DriveGrantRequest[]
            {
                new()
                {
                    PermissionedDrive = new()
                    {
                        Drive = appDrive,
                        Permission = DrivePermission.All
                    }
                }
            },
            PermissionSet = new PermissionSet(permissionKeys)
        });

        var def = await owner.Admin.GetCircleDefinition(circleId);

        if (owningAppId.HasValue)
        {
            var response = await owner.RefitFor<IRefitOwnerCircleDefinition>().SetCircleOwningApp(
                new SetCircleOwningAppRequest
                {
                    CircleId = circleId,
                    AppId = owningAppId.Value
                });
            Assert.That(response.IsSuccessStatusCode, Is.True,
                $"Failed to set owning app.  Actual response {response.StatusCode}");

            def.AppId = owningAppId.Value;
        }

        return def;
    }

    private static Task<AppSession> CreateAppAndClient(OwnerSession owner, params int[] permissionKeys)
    {
        return CreateAppAndClient(owner, Guid.NewGuid(), permissionKeys);
    }

    /// <summary>
    /// Same, with the app's id chosen by the caller -- needed when the test also has to create
    /// circles owned by that app.
    /// </summary>
    private static async Task<AppSession> CreateAppAndClient(OwnerSession owner, Guid appId,
        params int[] permissionKeys)
    {
        var appDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(appDrive, "Some Drive 1", allowAnonymousReads: false);

        return await AppSession.SetupAsync(owner, appDrive, DrivePermission.All, permissionKeys, knownAppId: appId);
    }

    private static Task<Refit.ApiResponse<List<CircleDomainResult>>> GetDomainsInCircle(AppSession app, Guid circleId)
    {
        return app.RefitFor<ICircleMembershipAppHttpClient>().GetDomainsInCircle(new GetCircleMembersRequest()
        {
            CircleId = circleId
        });
    }
}
