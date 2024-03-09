using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._Universal.DriveTests.Query;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class GuestQueryTests
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

    // [Test]
    // [Ignore("wip")]
    // public async Task QueryBatchEnforcesPermissionsOnAnonymousDrive()
    // {
      
        // // Sam Queries pippin; should get both files
        // var samGuestTokenFactory = await CreateGuestTokenFactory(TestIdentities.Samwise, frodoOwnerClient, targetDrive);
        // var samDriveClient = new UniversalDriveApiClient(TestIdentities.Pippin.OdinId, samGuestTokenFactory);
        // var samQueryResults = await samDriveClient.QueryBatch(query);
        // Assert.IsTrue(samQueryResults.IsSuccessStatusCode);
        // Assert.IsTrue(samQueryResults.Content.SearchResults.Count() == 2);
        //
        // // Merry queries Pippin; should get file2 only
        // var merryGuestTokenFactory = await CreateGuestTokenFactory(TestIdentities.Merry, frodoOwnerClient, targetDrive);
        // var merryDriveClient = new UniversalDriveApiClient(TestIdentities.Pippin.OdinId, merryGuestTokenFactory);
        // var merryQueryResults = await merryDriveClient.QueryBatch(query);
        // Assert.IsTrue(merryQueryResults.IsSuccessStatusCode);
        // Assert.IsTrue(merryQueryResults.Content.SearchResults.Count() == 1);

        //
    // }

    private async Task<IApiClientFactory> CreateGuestTokenFactory(TestIdentity guestIdentity, OwnerApiClientRedux ownerClient, TargetDrive targetDrive)
    {
        var driveGrants = new List<DriveGrantRequest>()
        {
            new()
            {
                PermissionedDrive = new()
                {
                    Drive = targetDrive,
                    Permission = DrivePermission.Read
                }
            }
        };

        var frodoCallerContextOnSam = new GuestAccess(guestIdentity.OdinId, driveGrants, new TestPermissionKeyList());
        await frodoCallerContextOnSam.Initialize(ownerClient);
        return frodoCallerContextOnSam.GetFactory();
    }

}