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
/// <c>_scaffold.Scenarios.CreateConnectedHobbits</c> becomes <see cref="PeerFlow.ConnectAllAsync"/>,
/// run once from <see cref="WarmTenantBaselineAsync"/> rather than per test — the mesh is connections,
/// circles and drives, all of which the baseline snapshot carries, so eighteen handshakes become six.
/// The original read the acting identity's mesh circle off the <c>ScenarioContext</c> that helper
/// returned; <c>ConnectAllAsync</c> generates the circle ids internally and returns nothing, so
/// <see cref="MeshCircleIdAsync"/> reads it back instead, identified by the drive it grants —
/// <see cref="HobbitMeshDrive"/>, which nothing else here creates, so exactly one circle can grant it.
/// (Counting circles would not do: a freshly initialised tenant already carries thirteen the app tree
/// declares.) The original also registered an
/// app per hobbit inside that helper; these three tests register their own app afterwards and never
/// touch the helper's, so that step is dropped.
/// </para>
/// <para>
/// Every hobbit test ended with <c>Scenarios.DisconnectHobbits()</c>, pure lifecycle restoration now
/// owned by per-test reset, so dropped.
/// </para>
/// <para>
/// The original pinned Frodo / Pippin / Merry per test purely because <c>WebScaffold</c> shared
/// identities process-wide. None of the assertions read which identity is acting, so every test runs
/// as the fixture default; <c>HostIdentities</c> lists four because the baked-in mesh spans all four.
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
        var appClient = await CreateAppAndClient(owner, PermissionKeys.All.ToArray());

        var response = await GetDomainsInCircle(appClient, SystemCircleConstants.ConfirmedConnectionsCircleId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppCanGetCircleMembers()
    {
        var owner = await LoginAsOwner();
        var meshCircleId = await MeshCircleIdAsync(owner);

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
        var meshCircleId = await MeshCircleIdAsync(owner);

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

    /// <summary>
    /// The four write verbs the original spelled out as <c>AppFailsTo{Update,Delete,Disable,Enable}
    /// CircleDefinition</c>. Each was the same four lines — register an app holding
    /// <c>ReadCircleMembership</c>, create a circle, call one endpoint, expect Forbidden — so they are
    /// rows here; the row name is the original test's name, so a failure still points at it.
    /// </summary>
    public static IEnumerable<TestCaseData> ForbiddenCircleWrites()
    {
        yield return Row("AppFailsToUpdateCircleDefinition", (client, def) =>
        {
            def.Name = "another name";
            return client.UpdateCircleDefinition(def);
        });
        yield return Row("AppFailsToDeleteCircleDefinition", (client, def) => client.DeleteCircleDefinition(def.Id.Value));
        yield return Row("AppFailsToDisableCircleDefinition", (client, def) => client.DisableCircleDefinition(def.Id.Value));
        yield return Row("AppFailsToEnableCircleDefinition", (client, def) => client.EnableCircleDefinition(def.Id.Value));

        static TestCaseData Row(
            string name,
            Func<IAppCircleDefinitionClient, CircleDefinition, Task<Refit.ApiResponse<bool>>> call)
            => new TestCaseData(call).SetArgDisplayNames(name);
    }

    [Test, TestCaseSource(nameof(ForbiddenCircleWrites))]
    public async Task AppFailsToWriteCircleDefinition(
        Func<IAppCircleDefinitionClient, CircleDefinition, Task<Refit.ApiResponse<bool>>> call)
    {
        var owner = await LoginAsOwner();
        var appClient = await CreateAppAndClient(owner, PermissionKeys.ReadCircleMembership);
        var def1 = await CreateRandomCircle(owner);

        var response = await call(appClient.RefitFor<IAppCircleDefinitionClient>(), def1);
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
    /// The drive the four-identity mesh is built over. Fixed rather than minted per test so the mesh
    /// can be baked into the baseline snapshot once (see <see cref="WarmTenantBaselineAsync"/>); it
    /// also has to be a drive no other arrange here creates, because
    /// <see cref="MeshCircleIdAsync"/> identifies the mesh circle by it.
    /// </summary>
    private static readonly TargetDrive HobbitMeshDrive = new()
    {
        Alias = Guid.Parse("a1b2c3d4-0000-4000-8000-000000000001"),
        Type = Guid.Parse("a1b2c3d4-0000-4000-8000-000000000002")
    };

    /// <summary>
    /// Bakes the four-identity mesh the original got from <c>Scenarios.CreateConnectedHobbits</c> into
    /// the baseline, minus the per-hobbit app registration nothing here reads. Connections, circles and
    /// drives are snapshot state, so the eighteen handshakes run once for the fixture rather than once
    /// per mesh test. The nine tests that do not read the mesh are unaffected: each looks its circles
    /// and definitions up by an id it minted itself, never by counting what the tenant holds.
    /// </summary>
    protected override async Task WarmTenantBaselineAsync()
    {
        await base.WarmTenantBaselineAsync();

        var hobbits = new List<OwnerSession>();
        foreach (var identity in HostIdentities)
        {
            hobbits.Add(await LoginAsOwner(identity));
        }

        await PeerFlow.ConnectAllAsync(hobbits, HobbitMeshDrive);
    }

    /// <summary>
    /// The circle <see cref="PeerFlow.ConnectAllAsync"/> created for this identity and granted to
    /// every peer, found by the one thing that distinguishes it: it is the circle that grants
    /// <see cref="HobbitMeshDrive"/>. See the class remarks for why it has to be read back at all.
    /// </summary>
    private static async Task<Guid> MeshCircleIdAsync(OwnerSession owner)
    {
        var response = await owner.RefitFor<IRefitOwnerCircleDefinition>().GetCircleDefinitions();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var matches = response.Content!
            .Where(d => d.DriveGrants != null &&
                        d.DriveGrants.Any(g => g.PermissionedDrive.Drive == HobbitMeshDrive))
            .ToList();

        Assert.That(matches.Count, Is.EqualTo(1),
            "exactly one circle should grant the drive the mesh connect was built over");
        return matches.Single().Id.Value;
    }

    /// <summary>
    /// A circle over a drive minted for it, optionally handed to <paramref name="owningAppId"/> once
    /// created. The create endpoint takes an AppId, but the owner-side helper this uses does not carry
    /// one, so the circle is adopted immediately afterwards instead.
    /// </summary>
    /// <remarks>
    /// The circle carries no permission keys. The original's helper took them, but no call site here
    /// ever supplied one, so the parameter has been dropped rather than carried as always-empty.
    /// </remarks>
    private static async Task<CircleDefinition> CreateRandomCircle(OwnerSession owner, Guid? owningAppId = null)
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
            PermissionSet = new PermissionSet()
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
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

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
    /// <remarks>
    /// Byte-for-byte the same body as <c>Ported/Transit/AppTransitClients.CreateAppAsync</c>, down to
    /// the drive label, bar this one's caller-chosen app id. Not merged here because that file is
    /// owned elsewhere; promoting one shared helper is tracked as follow-up work.
    /// </remarks>
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
