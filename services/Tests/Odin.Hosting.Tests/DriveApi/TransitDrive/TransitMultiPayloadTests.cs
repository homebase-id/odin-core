using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;

namespace Odin.Hosting.Tests.DriveApi.TransitDrive;

public class TransitMultiPayloadTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [Test]
    public async Task TransitSendsMultiplePayloads_When_SentViaDriveUpload()
    {
        //Create two connected hobbits

        var targetDrive1 = TargetDrive.NewTargetDrive();
        var targetDrive2 = TargetDrive.NewTargetDrive();

        var identity = TestIdentities.Samwise;
        var ownerSam = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        await ownerSam.Drive.CreateDrive(targetDrive1, "Drive 1 for Circle Test", "", false);
        await ownerSam.Drive.CreateDrive(targetDrive2, "Drive 2 for Circle Test", "", false);
        
        var dgr1 = new DriveGrantRequest()
        {
            PermissionedDrive = new PermissionedDrive()
            {
                Drive = targetDrive1,
                Permission = DrivePermission.ReadWrite
            }
        };

        var dgr2 = new DriveGrantRequest()
        {
            PermissionedDrive = new PermissionedDrive()
            {
                Drive = targetDrive1,
                Permission = DrivePermission.Write
            }
        };

        await ownerSam.Membership.CreateCircleRaw("Test Circle", new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>() { dgr1, dgr2 },
            PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections })
        });

        Assert.Inconclusive("TODO");
    }

    [Test]
    public void TransitSendsMultiplePayloads_When_SentViaTransitSender()
    {
        Assert.Inconclusive("TODO");
    }

    [Test]
    public void TransitDistributesUpdatesWhenAtLeastOnePayloadIsChanged()
    {
        //Note - i think this i actually required by the client ot just send the whole thing
        Assert.Inconclusive("TODO");
    }
}