using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;

namespace Odin.Hosting.Tests._Universal.Transit;

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

    public static IEnumerable TestCases()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.NotFound };
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.NotFound };
    }
    
    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task TransitSendsMultiplePayloads_When_SentViaDriveUpload(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        //Create two connected hobbits

        var targetDrive1 = callerContext.TargetDrive;
        var targetDrive2 = TargetDrive.NewTargetDrive();

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

}