using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.OwnerToken.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.V2.Ported.Circles;

/// <summary>
/// Port of <c>OwnerApi/Membership/Circles/CircleDefinitionTests</c>. What the owner console can say
/// about a circle definition: create it, list it, update it, disable it, delete it -- and the four
/// shapes the server refuses (an owner-only drive on create or on update, a circle that grants
/// neither a drive nor a permission on create or on update, and <c>UseTransit*</c> as a circle
/// permission).
/// </summary>
/// <remarks>
/// The circle-definition endpoints are V1 only, so the calls under test go through the V1 Refit
/// surface (<see cref="IRefitOwnerCircleDefinition"/>) over the in-process pipeline. Most of these
/// tests assert a refusal, and <c>owner.Admin</c> is arrange-only and throws on non-2xx, so the
/// system-under-test calls are made via <see cref="OwnerSession.RefitFor{T}"/>. Where a circle is
/// merely a precondition for the update/delete under test, <c>owner.Admin.CreateCircle</c> makes it
/// -- that helper spells the description as <c>"Description for {name}"</c> rather than the
/// original's <c>"Test circle description"</c>, which no assertion in those tests reads.
/// <para>
/// The original named Frodo and Samwise. Neither is load-bearing: Samwise was the acting owner
/// everywhere except <see cref="FailToCreateCircleWithUseTransitPermissions"/>, which used Frodo for
/// no reason the test states, and no test is cross-identity. The port runs entirely as the fixture's
/// default identity.
/// </para>
/// <para>
/// <c>SetupCallerWithOwner</c> ordering is not in play here -- this fixture has no caller matrix and
/// uses <c>LoginAsOwner</c> only.
/// </para>
/// <para>
/// <see cref="CanDisableCircle"/> re-read its *first* <c>GetCircleDefinitions</c> response after the
/// update, so every one of its post-update assertions compared the locally-mutated object against
/// itself -- which hid issue #1760, where the flag never persisted. It now disables through the
/// dedicated endpoint and re-reads from the server. Carried over unchanged:
/// <see cref="FailToUpdateInvalidCircle"/> prints the create response's status in the message for the
/// update assertion.
/// </para>
/// </remarks>
[TestFixture]
public class CircleDefinitionTests : V2Fixture
{
    [Test, Explicit("TODO")]
    public void SystemCircleUpdatedWhenAnonymousDriveAdded()
    {
        Assert.Inconclusive("TODO");
    }

    [Test, Explicit("TODO")]
    public void SystemCircleUpdatedWhenAnonymousDriveRemoved()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    public async Task FailToCreateCircleWithOwnerOnlyDrive()
    {
        var owner = await LoginAsOwner();

        var targetDrive1 = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(targetDrive1, "Owner Only for Circle Test",
            allowAnonymousReads: false, ownerOnly: true);

        var svc = owner.RefitFor<IRefitOwnerCircleDefinition>();

        var requestWithOwnerOnlyDrive = new CreateCircleRequest
        {
            Id = Guid.NewGuid(),
            Name = "Test Circle",
            Description = "Test circle description",
            DriveGrants = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = targetDrive1,
                        Permission = DrivePermission.Read
                    }
                }
            },
            Permissions = new PermissionSet()
        };

        var createCircleResponse = await svc.CreateCircleDefinition(requestWithOwnerOnlyDrive);
        Assert.That(createCircleResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task FailToUpdateExistingCircleDefinitionByAddingOwnerOnlyDrive()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerCircleDefinition>();

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Test Circle", new PermissionSetGrantRequest
        {
            Drives = null,
            PermissionSet = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
        });

        var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
        Assert.That(getCircleDefinitionsResponse.IsSuccessStatusCode, Is.True,
            $"Actual response {getCircleDefinitionsResponse.StatusCode}");

        var definitionList = getCircleDefinitionsResponse.Content;
        Assert.That(definitionList, Is.Not.Null);

        var circle = definitionList.Single(c => c.Id == circleId);
        Assert.That(circle.Permissions.Keys, Does.Contain(PermissionKeys.ReadCircleMembership));

        //Add an owner-only drive

        var targetDrive1 = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(targetDrive1, "Owner Only for Circle Test",
            allowAnonymousReads: false, ownerOnly: true);

        //

        circle.Name = "updated name";
        circle.Description = "updated description";

        circle.DriveGrants = new List<DriveGrantRequest>
        {
            new()
            {
                PermissionedDrive = new PermissionedDrive
                {
                    Drive = targetDrive1,
                    Permission = DrivePermission.Read
                }
            }
        };

        circle.Permissions = new PermissionSet(new List<int> { PermissionKeys.ReadConnections });

        var updateCircleResponse = await svc.UpdateCircleDefinition(circle);
        Assert.That(updateCircleResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        var getUpdatedCircleDefinitionsResponse = await svc.GetCircleDefinitions();
        Assert.That(getUpdatedCircleDefinitionsResponse.IsSuccessStatusCode, Is.True,
            $"Actual response {getUpdatedCircleDefinitionsResponse.StatusCode}");

        var updatedDefinitionList = getUpdatedCircleDefinitionsResponse.Content;
        Assert.That(updatedDefinitionList, Is.Not.Null);

        var circle2 = updatedDefinitionList.Single(c => c.Id == circleId);

        Assert.That(circle2.Name, Is.Not.EqualTo(circle.Name));
        Assert.That(circle2.Description, Is.Not.EqualTo(circle.Description));
        Assert.That(circle2.DriveGrants, Is.Not.EqualTo(circle.DriveGrants));
        Assert.That(circle2.Permissions, Is.Not.EqualTo(circle.Permissions));

        await svc.DeleteCircleDefinition(circle.Id);

        //TODO: test that the changes to the drives and permissions were applied
    }

    [Test]
    public async Task FailToCreateInvalidCircle()
    {
        var owner = await LoginAsOwner();

        var targetDrive1 = TargetDrive.NewTargetDrive();
        var targetDrive2 = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(targetDrive1, "Drive 1 for Circle Test", allowAnonymousReads: false);
        await owner.Admin.CreateDrive(targetDrive2, "Drive 2 for Circle Test", allowAnonymousReads: false);

        var svc = owner.RefitFor<IRefitOwnerCircleDefinition>();

        var requestWithNoPermissionsOrDrives = new CreateCircleRequest
        {
            Id = Guid.NewGuid(),
            Name = "Test Circle",
            Description = "Test circle description",
            DriveGrants = new List<DriveGrantRequest>(),
            Permissions = new PermissionSet()
        };

        var createCircleResponse = await svc.CreateCircleDefinition(requestWithNoPermissionsOrDrives);
        Assert.That(createCircleResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var code = TestUtils.ParseProblemDetails(createCircleResponse.Error!);
        Assert.That(code, Is.EqualTo(OdinClientErrorCode.AtLeastOneDriveOrPermissionRequiredForCircle));
    }

    [Test]
    public async Task FailToCreateCircleWithUseTransitPermissions()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerCircleDefinition>();

        const string circleName = "Circle with UseTransit";
        var createCircleResponse = await svc.CreateCircleDefinition(new CreateCircleRequest
        {
            Id = Guid.NewGuid(),
            Name = circleName,
            Description = $"Description for {circleName}",
            DriveGrants = null,
            Permissions = new PermissionSet(new[] { PermissionKeys.UseTransitWrite })
        });

        Assert.That(createCircleResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CanCreateCircle()
    {
        var owner = await LoginAsOwner();

        var targetDrive1 = TargetDrive.NewTargetDrive();
        var targetDrive2 = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(targetDrive1, "Drive 1 for Circle Test", allowAnonymousReads: false);
        await owner.Admin.CreateDrive(targetDrive2, "Drive 2 for Circle Test", allowAnonymousReads: false);

        var svc = owner.RefitFor<IRefitOwnerCircleDefinition>();

        var dgr1 = new DriveGrantRequest
        {
            PermissionedDrive = new PermissionedDrive
            {
                Drive = targetDrive1,
                Permission = DrivePermission.ReadWrite
            }
        };

        var dgr2 = new DriveGrantRequest
        {
            PermissionedDrive = new PermissionedDrive
            {
                Drive = targetDrive2,
                Permission = DrivePermission.Write
            }
        };

        var request = new CreateCircleRequest
        {
            Id = Guid.NewGuid(),
            Name = "Test Circle",
            Description = "Test circle description",
            DriveGrants = new List<DriveGrantRequest> { dgr1, dgr2 },
            Permissions = new PermissionSet(new List<int>
                { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections })
        };

        var createCircleResponse = await svc.CreateCircleDefinition(request);
        Assert.That(createCircleResponse.IsSuccessStatusCode, Is.True,
            $"Actual response {createCircleResponse.StatusCode}");

        var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
        Assert.That(getCircleDefinitionsResponse.IsSuccessStatusCode, Is.True,
            $"Actual response {getCircleDefinitionsResponse.StatusCode}");

        var definitionList = getCircleDefinitionsResponse.Content;
        Assert.That(definitionList, Is.Not.Null);

        var circle = definitionList.Single(c => c.Id == request.Id);

        Assert.That(circle.DriveGrants.SingleOrDefault(d => d.PermissionedDrive == dgr1.PermissionedDrive),
            Is.Not.Null);
        Assert.That(circle.DriveGrants.SingleOrDefault(d => d.PermissionedDrive == dgr2.PermissionedDrive),
            Is.Not.Null);

        Assert.That(circle.Permissions.Keys, Does.Contain(PermissionKeys.ReadCircleMembership));
        Assert.That(circle.Permissions.Keys, Does.Contain(PermissionKeys.ReadConnections));

        Assert.That(circle.Name, Is.EqualTo(request.Name));
        Assert.That(circle.Description, Is.EqualTo(request.Description));
        Assert.That(circle.Permissions, Is.EqualTo(request.Permissions));

        // cleanup
        await svc.DeleteCircleDefinition(circle.Id);
    }

    [Test]
    public async Task CanGetListOfCircleDefinitions()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerCircleDefinition>();

        var request1 = new CreateCircleRequest
        {
            Id = Guid.NewGuid(),
            Name = "Test Circle 1",
            Description = "Test circle description 1",
            DriveGrants = null,
            Permissions = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
        };

        var request2 = new CreateCircleRequest
        {
            Id = Guid.NewGuid(),
            Name = "Test Circle 2",
            Description = "Test circle description 2",
            DriveGrants = null,
            Permissions = new PermissionSet(new List<int>
                { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections })
        };

        try
        {
            var createCircleResponse1 = await svc.CreateCircleDefinition(request1);
            Assert.That(createCircleResponse1.IsSuccessStatusCode, Is.True,
                $"Actual response {createCircleResponse1.StatusCode}");

            var createCircleResponse2 = await svc.CreateCircleDefinition(request2);
            Assert.That(createCircleResponse2.IsSuccessStatusCode, Is.True,
                $"Actual response {createCircleResponse2.StatusCode}");

            var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
            Assert.That(getCircleDefinitionsResponse.IsSuccessStatusCode, Is.True,
                $"Actual response {getCircleDefinitionsResponse.StatusCode}");

            Assert.That(getCircleDefinitionsResponse.Content, Is.Not.Null);
            var definitionList = getCircleDefinitionsResponse.Content.ToList();

            // The owner's list is the two circles made here plus every circle the app tree
            // provisions. Counted from the tree rather than written down, so adding an app's
            // circle does not silently break an unrelated assertion.
            var seeded = BuiltinApps.SeededCircles.Select(c => (Guid)c.Id).Distinct().Count();
            Assert.That(definitionList.Count, Is.EqualTo(2 + seeded));

            var circle1 = definitionList.Single(x => x.Name == request1.Name);
            Assert.That(circle1.Name, Is.EqualTo(request1.Name));
            Assert.That(circle1.Description, Is.EqualTo(request1.Description));
            Assert.That(circle1.DriveGrants, Is.EqualTo(request1.DriveGrants));
            Assert.That(circle1.Permissions, Is.EqualTo(request1.Permissions));

            var circle2 = definitionList.Single(x => x.Name == request2.Name);
            Assert.That(circle2.Name, Is.EqualTo(request2.Name));
            Assert.That(circle2.Description, Is.EqualTo(request2.Description));
            Assert.That(circle2.DriveGrants, Is.EqualTo(request2.DriveGrants));
            Assert.That(circle2.Permissions, Is.EqualTo(request2.Permissions));
        }
        finally
        {
            // cleanup
            await svc.DeleteCircleDefinition(request1.Id);
            await svc.DeleteCircleDefinition(request2.Id);
        }
    }

    [Test]
    public async Task CanUpdateCircleDefinition_NoMembershipReconciliation()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerCircleDefinition>();

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Test Circle", new PermissionSetGrantRequest
        {
            Drives = null,
            PermissionSet = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
        });

        var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
        Assert.That(getCircleDefinitionsResponse.IsSuccessStatusCode, Is.True,
            $"Actual response {getCircleDefinitionsResponse.StatusCode}");

        var definitionList = getCircleDefinitionsResponse.Content;
        Assert.That(definitionList, Is.Not.Null);

        var circle = definitionList.Single(c => c.Id == circleId);
        Assert.That(circle.Permissions.Keys, Does.Contain(PermissionKeys.ReadCircleMembership));

        //

        circle.Name = "updated name";
        circle.Description = "updated description";

        circle.DriveGrants = null;
        circle.Permissions = new PermissionSet(new List<int> { PermissionKeys.ReadConnections });

        var updateCircleResponse = await svc.UpdateCircleDefinition(circle);
        Assert.That(updateCircleResponse.IsSuccessStatusCode, Is.True,
            $"Actual response {updateCircleResponse.StatusCode}");

        var getUpdatedCircleDefinitionsResponse = await svc.GetCircleDefinitions();
        Assert.That(getUpdatedCircleDefinitionsResponse.IsSuccessStatusCode, Is.True,
            $"Actual response {getUpdatedCircleDefinitionsResponse.StatusCode}");

        var updatedDefinitionList = getUpdatedCircleDefinitionsResponse.Content;
        Assert.That(updatedDefinitionList, Is.Not.Null);

        var circle2 = updatedDefinitionList.Single(c => c.Id == circleId);

        Assert.That(circle2.Name, Is.EqualTo(circle.Name));
        Assert.That(circle2.Description, Is.EqualTo(circle.Description));
        Assert.That(circle2.DriveGrants, Is.EqualTo(circle.DriveGrants));
        Assert.That(circle2.Permissions, Is.EqualTo(circle.Permissions));

        await svc.DeleteCircleDefinition(circle.Id);

        //TODO: test that the changes to the drives and permissions were applied
    }

    [Test]
    public async Task CanDisableCircle()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerCircleDefinition>();

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Test Circle", new PermissionSetGrantRequest
        {
            Drives = null,
            PermissionSet = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
        });

        var circle = await GetCircle(svc, circleId);
        Assert.That(circle.Disabled, Is.False);

        var disableResponse = await svc.DisableCircleDefinition(circleId);
        Assert.That(disableResponse.IsSuccessStatusCode, Is.True, $"Actual response {disableResponse.StatusCode}");

        var disabledCircle = await GetCircle(svc, circleId);
        Assert.That(disabledCircle.Disabled, Is.True);
        Assert.That(disabledCircle.Name, Is.EqualTo(circle.Name));
        Assert.That(disabledCircle.Description, Is.EqualTo(circle.Description));
        Assert.That(disabledCircle.DriveGrants, Is.EqualTo(circle.DriveGrants));
        Assert.That(disabledCircle.Permissions, Is.EqualTo(circle.Permissions));

        // Update keeps the stored flag: a definition that says Disabled = false must not re-enable it.
        disabledCircle.Disabled = false;
        disabledCircle.Description = "changed while disabled";
        var updateResponse = await svc.UpdateCircleDefinition(disabledCircle);
        Assert.That(updateResponse.IsSuccessStatusCode, Is.True, $"Actual response {updateResponse.StatusCode}");

        var updatedCircle = await GetCircle(svc, circleId);
        Assert.That(updatedCircle.Disabled, Is.True);
        Assert.That(updatedCircle.Description, Is.EqualTo("changed while disabled"));

        var enableResponse = await svc.EnableCircleDefinition(circleId);
        Assert.That(enableResponse.IsSuccessStatusCode, Is.True, $"Actual response {enableResponse.StatusCode}");

        var enabledCircle = await GetCircle(svc, circleId);
        Assert.That(enabledCircle.Disabled, Is.False);

        await svc.DeleteCircleDefinition(circleId);
    }

    [Test]
    public async Task OwnerCanDisableAppOwnedCircle()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerCircleDefinition>();

        var appId = Guid.NewGuid();
        var appDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(appDrive, "App drive", allowAnonymousReads: false);
        await AppSession.SetupAsync(owner, appDrive, DrivePermission.Read,
            [PermissionKeys.ReadCircleMembership], knownAppId: appId);

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "App circle", new PermissionSetGrantRequest
        {
            Drives = null,
            PermissionSet = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
        });
        var setOwner = await svc.SetCircleOwningApp(new SetCircleOwningAppRequest { CircleId = circleId, AppId = appId });
        Assert.That(setOwner.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var response = await svc.DisableCircleDefinition(circleId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await GetCircle(svc, circleId)).Disabled, Is.True);
    }

    private static IEnumerable<Guid> SystemCircles() => SystemCircleConstants.AllSystemCircles.Select(c => c.Value);

    [Test, TestCaseSource(nameof(SystemCircles))]
    public async Task FailToDisableSystemCircle(Guid id)
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerCircleDefinition>();

        var response = await svc.DisableCircleDefinition(id);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var circles = await svc.GetCircleDefinitions(includeSystemCircle: true);
        Assert.That(circles.Content!.Single(c => c.Id == id).Disabled, Is.False);
    }

    [Test]
    public async Task FailToDisableUnknownCircle()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerCircleDefinition>();

        var response = await svc.DisableCircleDefinition(Guid.NewGuid());
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private static async Task<CircleDefinition> GetCircle(IRefitOwnerCircleDefinition svc, Guid circleId)
    {
        var response = await svc.GetCircleDefinitions();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        return response.Content.Single(c => c.Id == circleId);
    }

    [Test]
    public async Task FailToUpdateInvalidCircle()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerCircleDefinition>();

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Test Circle", new PermissionSetGrantRequest
        {
            Drives = null,
            PermissionSet = new PermissionSet(new List<int> { PermissionKeys.ReadConnections })
        });

        var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
        Assert.That(getCircleDefinitionsResponse.IsSuccessStatusCode, Is.True,
            $"Actual response {getCircleDefinitionsResponse.StatusCode}");

        var definitionList = getCircleDefinitionsResponse.Content;
        Assert.That(definitionList, Is.Not.Null);

        var circle = definitionList.Single(c => c.Id == circleId);

        //

        circle.Name = "updated name";
        circle.Description = "updated description";

        circle.DriveGrants = null;
        circle.Permissions = null;

        var updateCircleResponse = await svc.UpdateCircleDefinition(circle);

        Assert.That(updateCircleResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var code = TestUtils.ParseProblemDetails(updateCircleResponse.Error!);
        Assert.That(code, Is.EqualTo(OdinClientErrorCode.AtLeastOneDriveOrPermissionRequiredForCircle));

        await svc.DeleteCircleDefinition(circle.Id);
    }

    [Test]
    public async Task CanDeleteCircle()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerCircleDefinition>();

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Test Circle", new PermissionSetGrantRequest
        {
            Drives = null,
            PermissionSet = new PermissionSet(new List<int>
                { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections })
        });

        var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
        Assert.That(getCircleDefinitionsResponse.IsSuccessStatusCode, Is.True,
            $"Actual response {getCircleDefinitionsResponse.StatusCode}");

        var definitionList = getCircleDefinitionsResponse.Content;
        Assert.That(definitionList, Is.Not.Null);

        var id = definitionList.Single(c => c.Id == circleId).Id;
        var deleteCircleResponse = await svc.DeleteCircleDefinition(id);
        Assert.That(deleteCircleResponse.IsSuccessStatusCode, Is.True,
            $"Actual response {deleteCircleResponse.StatusCode}");

        var secondGetCircleDefinitionsResponse = await svc.GetCircleDefinitions();
        Assert.That(secondGetCircleDefinitionsResponse.IsSuccessStatusCode, Is.True,
            $"Actual response {secondGetCircleDefinitionsResponse.StatusCode}");
        var remainingDefinitionList = secondGetCircleDefinitionsResponse.Content;
        Assert.That(remainingDefinitionList, Is.Not.Null);
        // The deleted user circle is gone; only built-in circles remain.
        Assert.That(remainingDefinitionList.Any(c => c.Id == circleId), Is.False);
    }
}
