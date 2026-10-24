using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Circles;

/// <summary>
/// Circles an app declares in its install request (#1939, step 13): created with the app, owned by it, and
/// bounded by what the registration grants it.  Optional -- every other registration in the suite declares none.
/// </summary>
[TestFixture]
public class AppCircleDeclarationTests : V2Fixture
{
    [Test]
    public async Task ADeclaredCircleIsCreatedWithTheApp()
    {
        var owner = await LoginAsOwner();
        var drive = await CreateDriveAsync(owner);
        var appId = Guid.NewGuid();

        var response = await Register(owner, appId, drive, DrivePermission.Read, PermissionKeys.ReadConnections,
            Declared("sync", grants: [Grant(drive, DrivePermission.Read)], keys: [PermissionKeys.ReadConnections]));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"register: {response.Error?.Content}");

        var circles = (await owner.RefitFor<IRefitOwnerCircleDefinition>().GetCircleDefinitions()).Content
            .Where(c => c.AppId == appId).ToList();
        Assert.That(circles, Has.Count.EqualTo(1));

        var circle = circles.Single();
        Assert.That(circle.Slug, Is.EqualTo("sync"));
        Assert.That(circle.Name, Is.EqualTo("Declared sync"));
        Assert.That(circle.GrantOn, Is.EqualTo(CircleGrantOn.None));
        Assert.That(circle.DriveGrants.Single().PermissionedDrive.Drive, Is.EqualTo(drive));
        Assert.That(circle.Permissions.Keys, Is.EquivalentTo(new[] { PermissionKeys.ReadConnections }));

        // A later device's circles/create with the same slug finds it rather than making a second one.
        var app = await AppSession.ForRegisteredAppAsync(owner, appId);
        var create = await app.RefitFor<IConnectionNetworkHttpClientApiV2>().CreateCircle(new CreateAppCircleRequest
        {
            Name = "Declared sync",
            Slug = new OdinSlug("sync"),
            Permissions = new PermissionSet(PermissionKeys.ReadConnections)
        });
        Assert.That(create.Content, Is.EqualTo((Guid)circle.Id));
    }

    [Test]
    public async Task ACircleGrantingAKeyTheRegistrationDoesNotIsRefusedAndTheAppIsNotRegistered()
    {
        var owner = await LoginAsOwner();
        var drive = await CreateDriveAsync(owner);
        var appId = Guid.NewGuid();

        var response = await Register(owner, appId, drive, DrivePermission.Read, PermissionKeys.ReadConnections,
            Declared("too-much", grants: [], keys: [PermissionKeys.ReadWhoIFollow]));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        await AssertNotRegisteredAsync(owner, appId);
    }

    [Test]
    public async Task ACircleGrantingMoreOnADriveThanTheRegistrationIsRefused()
    {
        var owner = await LoginAsOwner();
        var drive = await CreateDriveAsync(owner);
        var appId = Guid.NewGuid();

        var response = await Register(owner, appId, drive, DrivePermission.Read, PermissionKeys.ReadConnections,
            Declared("writers", grants: [Grant(drive, DrivePermission.Write)], keys: []));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        await AssertNotRegisteredAsync(owner, appId);
    }

    [Test]
    public async Task OnADriveTheAppDoesNotOwnACircleMayGrantOnlyRead()
    {
        var owner = await LoginAsOwner();
        var drive = await CreateDriveAsync(owner);
        var appId = Guid.NewGuid();

        // The registration holds write, but the drive is the owner's: write there would let the app's contacts
        // change data the app does not own.
        var response = await Register(owner, appId, drive, DrivePermission.ReadWrite, PermissionKeys.ReadConnections,
            Declared("writers", grants: [Grant(drive, DrivePermission.Write)], keys: []));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        await AssertNotRegisteredAsync(owner, appId);
    }

    [Test]
    public async Task AMalformedOrRepeatedSlugIsRefused()
    {
        var owner = await LoginAsOwner();
        var drive = await CreateDriveAsync(owner);

        // Untyped: an OdinSlug can't hold it
        var appId = Guid.NewGuid();
        var malformed = await owner.RefitFor<IRawSlugRequests>().RegisterApp(new System.Collections.Generic.Dictionary<string, object>
        {
            ["appId"] = appId,
            ["name"] = $"Test_{appId}",
            ["appSlug"] = OwnerAdmin.SlugFor(appId).Value,
            ["permissionSet"] = new PermissionSet(PermissionKeys.ReadConnections),
            ["drives"] = new[] { Grant(drive, DrivePermission.Read) },
            ["authorizedCircles"] = Array.Empty<Guid>(),
            ["circleMemberPermissionGrant"] = new PermissionSetGrantRequest(),
            ["circles"] = new[]
            {
                new System.Collections.Generic.Dictionary<string, object>
                {
                    ["slug"] = "Not A Slug",
                    ["name"] = "Declared Not A Slug",
                    ["permissions"] = new PermissionSet(PermissionKeys.ReadConnections)
                }
            }
        });
        Assert.That(malformed.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), malformed.Error?.Content);

        var repeated = await Register(owner, Guid.NewGuid(), drive, DrivePermission.Read, PermissionKeys.ReadConnections,
            Declared("twice", grants: [], keys: [PermissionKeys.ReadConnections]),
            Declared("twice", grants: [], keys: [PermissionKeys.ReadConnections]));
        Assert.That(repeated.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public void TheDeclarationSurvivesTheRedirectToTheInstallPage()
    {
        // The authorize endpoint rebuilds the install URL from these parameters; a key missing from either side
        // of the round trip is dropped there without a word.
        var parameters = new YouAuthAppParameters
        {
            AppId = Guid.NewGuid().ToString(),
            AppName = "Some app",
            ClientFriendly = "Firefox | macOS",
            DrivesParam = "[]",
            AppCirclesParam = "[{\"s\":\"sync\",\"n\":\"Sync\"}]",
        };

        var roundTripped = YouAuthAppParameters.FromQueryString(parameters.ToQueryString());
        Assert.That(roundTripped.AppCirclesParam, Is.EqualTo(parameters.AppCirclesParam));
    }

    [Test]
    public void WithoutADeclarationTheInstallUrlIsUnchanged()
    {
        var parameters = new YouAuthAppParameters { AppId = "a", AppName = "n", ClientFriendly = "f", DrivesParam = "[]" };
        Assert.That(parameters.ToQueryString(), Does.Not.Contain(YouAuthAppParameters.AppCirclesParamName + "="));
    }

    //

    private static CreateAppCircleRequest Declared(string slug, List<DriveGrantRequest> grants, int[] keys) => new()
    {
        Slug = slug == null ? null : new OdinSlug(slug),
        Name = "Declared " + slug,
        DriveGrants = grants,
        Permissions = new PermissionSet(keys)
    };

    private static DriveGrantRequest Grant(TargetDrive drive, DrivePermission permission) => new()
    {
        PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = permission }
    };

    private static async Task<TargetDrive> CreateDriveAsync(OwnerSession owner)
    {
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Owner's drive", allowAnonymousReads: false);
        return drive;
    }

    private static Task<ApiResponse<RedactedAppRegistration>> Register(OwnerSession owner, Guid appId, TargetDrive drive,
        DrivePermission access, int key, params CreateAppCircleRequest[] circles) =>
        owner.RefitFor<IRefitOwnerAppRegistration>().RegisterApp(new AppRegistrationRequest
        {
            AppId = appId,
            Name = $"Test_{appId}",
            AppSlug = OwnerAdmin.SlugFor(appId),
            PermissionSet = new PermissionSet(key),
            Drives = [Grant(drive, access)],
            AuthorizedCircles = [],
            CircleMemberPermissionGrant = new PermissionSetGrantRequest(),
            Circles = circles.ToList()
        });

    private static async Task AssertNotRegisteredAsync(OwnerSession owner, Guid appId)
    {
        var app = await owner.RefitFor<IRefitOwnerAppRegistration>().GetRegisteredApp(new GetAppRequest { AppId = appId });
        Assert.That(app.Content, Is.Null, "a refused declaration must refuse the install, not leave the app behind");
    }
}
