using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Hosting.Tests.AppAPI.ApiClient;

namespace Odin.Hosting.Tests.AppAPI.Membership;

public class AppCircleDefinitionTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Samwise, TestIdentities.Merry, TestIdentities.Pippin });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }


    [Test]
    public async Task AppCannotSeeSystemCircleMembers()
    {
        var targetDrive = TargetDrive.NewTargetDrive();
        await _scaffold.Scenarios.CreateConnectedHobbits(targetDrive);

        var appClient = await this.CreateAppAndClient(TestIdentities.Frodo, PermissionKeys.All.ToArray());

        var response = await appClient.CircleNetwork.GetDomainsInCircle(SystemCircleConstants.ConfirmedConnectionsCircleId);
        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden);

        await _scaffold.Scenarios.DisconnectHobbits();
    }

    [Test]
    public async Task AppCanGetCircleMembers()
    {
        var identity = TestIdentities.Frodo;
        var targetDrive = TargetDrive.NewTargetDrive();
        var ctx = await _scaffold.Scenarios.CreateConnectedHobbits(targetDrive);

        var appClient = await this.CreateAppAndClient(identity, PermissionKeys.ReadCircleMembership);

        var response = await appClient.CircleNetwork.GetDomainsInCircle(ctx.Circles[identity.OdinId].Id);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        var list = response.Content;
        ClassicAssert.IsNotNull(list);
        ClassicAssert.IsNotNull(list.SingleOrDefault(cdr => cdr.Domain.DomainName == TestIdentities.Samwise.OdinId));
        ClassicAssert.IsNotNull(list.SingleOrDefault(cdr => cdr.Domain.DomainName == TestIdentities.Merry.OdinId));
        ClassicAssert.IsNotNull(list.SingleOrDefault(cdr => cdr.Domain.DomainName == TestIdentities.Pippin.OdinId));

        await _scaffold.Scenarios.DisconnectHobbits();
    }

    [Test]
    public async Task AppFailsToGetCircleMembersWithoutReadCircleMembershipPermission()
    {
        var identity = TestIdentities.Frodo;
        var targetDrive = TargetDrive.NewTargetDrive();
        var ctx = await _scaffold.Scenarios.CreateConnectedHobbits(targetDrive);

        var appClient = await this.CreateAppAndClient(identity, PermissionKeys.ReadConnections);

        var response = await appClient.CircleNetwork.GetDomainsInCircle(ctx.Circles[identity.OdinId].Id);
        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden);

        await _scaffold.Scenarios.DisconnectHobbits();
    }

    [Test]
    public async Task AppCanGetCircleDefinition()
    {
        var identity = TestIdentities.Pippin;
        var def = await this.CreateRandomCircle(identity);

        var appClient = await this.CreateAppAndClient(identity, PermissionKeys.ReadCircleMembership);

        var getDefinitionResponse = await appClient.CircleDefinitions.GetCircleDefinition(def.Id);
        ClassicAssert.IsTrue(getDefinitionResponse.IsSuccessStatusCode);
        ClassicAssert.IsNotNull(getDefinitionResponse.Content);
        ClassicAssert.IsTrue(getDefinitionResponse.Content.Id == def.Id);
        ClassicAssert.IsTrue(getDefinitionResponse.Content.Name == def.Name);
    }

    [Test]
    public async Task AppFailsToGetCircleDefinitionWithoutReadCircleMembershipPermission()
    {
        var identity = TestIdentities.Pippin;
        var def = await this.CreateRandomCircle(identity);

        var appClient = await this.CreateAppAndClient(identity);

        var getDefinitionResponse = await appClient.CircleDefinitions.GetCircleDefinition(def.Id);
        ClassicAssert.IsTrue(getDefinitionResponse.StatusCode == HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AppCanGetCircleDefinitionList()
    {
        var identity = TestIdentities.Merry;

        var appClient = await this.CreateAppAndClient(identity, PermissionKeys.ReadCircleMembership);

        var def1 = await this.CreateRandomCircle(identity);
        var def2 = await this.CreateRandomCircle(identity);
        var def3 = await this.CreateRandomCircle(identity);

        var getDefinitionResponse = await appClient.CircleDefinitions.GetCircleDefinitions();
        ClassicAssert.IsTrue(getDefinitionResponse.IsSuccessStatusCode);
        ClassicAssert.IsNotNull(getDefinitionResponse.Content);
        ClassicAssert.IsNotNull(getDefinitionResponse.Content.SingleOrDefault(d => d.Id == def1.Id));
        ClassicAssert.IsNotNull(getDefinitionResponse.Content.SingleOrDefault(d => d.Id == def2.Id));
        ClassicAssert.IsNotNull(getDefinitionResponse.Content.SingleOrDefault(d => d.Id == def3.Id));
    }

    [Test]
    public async Task AppFailsGetCircleDefinitionListWithoutReadCircleMembershipPermission()
    {
        var identity = TestIdentities.Merry;

        var appClient = await this.CreateAppAndClient(identity);

        await this.CreateRandomCircle(identity);
        await this.CreateRandomCircle(identity);
        await this.CreateRandomCircle(identity);

        var getDefinitionResponse = await appClient.CircleDefinitions.GetCircleDefinitions();
        ClassicAssert.IsTrue(getDefinitionResponse.StatusCode == HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AppFailsToUpdateCircleDefinition()
    {
        var identity = TestIdentities.Merry;
        var appClient = await this.CreateAppAndClient(identity, PermissionKeys.ReadCircleMembership);
        var def1 = await this.CreateRandomCircle(identity);
        def1.Name = "another name";
        var response = await appClient.CircleDefinitions.Update(def1);
        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AppFailsToDeleteCircleDefinition()
    {
        var identity = TestIdentities.Merry;
        var appClient = await this.CreateAppAndClient(identity, PermissionKeys.ReadCircleMembership);
        var def1 = await this.CreateRandomCircle(identity);
        var response = await appClient.CircleDefinitions.Delete(def1.Id);
        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AppFailsToDisableCircleDefinition()
    {
        var identity = TestIdentities.Merry;
        var appClient = await this.CreateAppAndClient(identity, PermissionKeys.ReadCircleMembership);
        var def1 = await this.CreateRandomCircle(identity);
        var response = await appClient.CircleDefinitions.Disable(def1.Id);
        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AppFailsToEnableCircleDefinition()
    {
        var identity = TestIdentities.Merry;
        var appClient = await this.CreateAppAndClient(identity, PermissionKeys.ReadCircleMembership);
        var def1 = await this.CreateRandomCircle(identity);
        var response = await appClient.CircleDefinitions.Enable(def1.Id);
        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AppFailsToCreateCircleDefinition()
    {
        var identity = TestIdentities.Merry;
        var appClient = await this.CreateAppAndClient(identity, PermissionKeys.ReadCircleMembership);
        var response = await appClient.CircleDefinitions.Create(new CreateCircleRequest()
        {
            Id = Guid.NewGuid(),
            Name = "test",
            DriveGrants = default,
            Description = "test"
        });

        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden);
    }

    private async Task<CircleDefinition> CreateRandomCircle(TestIdentity identity, params int[] permissionKeys)
    {
        var titleId = Guid.NewGuid();
        var ownerClient = _scaffold.CreateOwnerApiClient(identity);

        var appDrive = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), $"Drive for {titleId}", "", false);

        var def = await ownerClient.Membership.CreateCircle($"Random circle {titleId}", new PermissionSetGrantRequest()
        {
            Drives = new DriveGrantRequest[]
            {
                new()
                {
                    PermissionedDrive = new()
                    {
                        Drive = appDrive.TargetDriveInfo,
                        Permission = DrivePermission.All
                    }
                }
            },
            PermissionSet = new PermissionSet(permissionKeys)
        });

        return def;
    }

    private async Task<AppApiClient> CreateAppAndClient(TestIdentity identity, params int[] permissionKeys)
    {
        var appId = Guid.NewGuid();

        var ownerClient = _scaffold.CreateOwnerApiClient(identity);

        var appDrive = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some Drive 1", "", false);

        var appPermissionsGrant = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = appDrive.TargetDriveInfo,
                        Permission = DrivePermission.All
                    }
                }
            },
            PermissionSet = new PermissionSet(permissionKeys)
        };

        await ownerClient.Apps.RegisterApp(appId, appPermissionsGrant);

        var client = _scaffold.CreateAppClient(identity, appId);
        return client;
    }
}