using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;

namespace Odin.Hosting.Tests._Universal.DriveTests.Query;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDriveQueryTests
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
    public async Task QueryBatchEnforcesPermissions()
    {
        // Setup

        var pippinOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var targetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await pippinOwnerClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var circle1 = Guid.NewGuid();
        await pippinOwnerClient.Network.CreateCircle(circle1, "secured circle", new PermissionSetGrantRequest()
        {
            Drives =
            [
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive() { Drive = targetDrive, Permission = DrivePermission.Read },
                },
            ]
        });

        // Connect sam and pippin; giving sam circle1
        await pippinOwnerClient.Connections.SendConnectionRequest(TestIdentities.Samwise.OdinId, new List<GuidId>() { circle1 });
        await samOwnerClient.Connections.AcceptConnectionRequest(TestIdentities.Pippin.OdinId);

        // Connect merry and pippin; no circles
        await pippinOwnerClient.Connections.SendConnectionRequest(TestIdentities.Merry.OdinId);
        await merryOwnerClient.Connections.AcceptConnectionRequest(TestIdentities.Pippin.OdinId);

        const int fileType = 1090;
        // Pippin uploads files
        // file 1: only connect identities in a specific circle 
        var file1 = SampleMetadataData.Create(fileType: fileType,
            acl: new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected, CircleIdList = [circle1] });
        var file1UploadResult = await UploadAndValidate(file1, targetDrive);

        // file 2: only connected identities can see it (a circle is not required)
        var file2 = SampleMetadataData.Create(fileType: fileType, acl: new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected });
        var file2UploadResult = await UploadAndValidate(file2, targetDrive);

        // Act

        var query = new QueryBatchRequest()
        {
            QueryParams = new FileQueryParams()
            {
                TargetDrive = targetDrive,
                FileType = [fileType]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest()
            {
                MaxRecords = 100,
                IncludeMetadataHeader = true
            }
        };

        // Assert

        // Sam Queries pippin; should get both files
        var samGuestTokenFactory = await CreateGuestTokenFactory(TestIdentities.Samwise, pippinOwnerClient, targetDrive);
        var samDriveClient = new UniversalDriveApiClient(TestIdentities.Pippin.OdinId, samGuestTokenFactory);
        var samQueryResults = await samDriveClient.QueryBatch(query);
        Assert.IsTrue(samQueryResults.IsSuccessStatusCode);
        Assert.IsTrue(samQueryResults.Content.SearchResults.Count() == 2);

        // Merry queries Pippin; should get file2 only
        var merryGuestTokenFactory = await CreateGuestTokenFactory(TestIdentities.Merry, pippinOwnerClient, targetDrive);
        var merryDriveClient = new UniversalDriveApiClient(TestIdentities.Pippin.OdinId, merryGuestTokenFactory);
        var merryQueryResults = await merryDriveClient.QueryBatch(query);
        Assert.IsTrue(merryQueryResults.IsSuccessStatusCode);
        Assert.IsTrue(merryQueryResults.Content.SearchResults.Count() == 1);
        //

    }


    private async Task<IApiClientFactory> CreateGuestTokenFactory(TestIdentity guestIdentity, OwnerApiClientRedux client, TargetDrive targetDrive)
    {
        var driveGrants = new List<DriveGrantRequest>()
        {
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = targetDrive,
                    Permission = DrivePermission.Read
                }
            }
        };
        // Login to Frodo's identity as Sam
        var frodoCallerContextOnSam = new GuestAccess(guestIdentity.OdinId, driveGrants, new TestPermissionKeyList());
        await frodoCallerContextOnSam.Initialize(client);

        return frodoCallerContextOnSam.GetFactory();
    }

    private async Task<UploadResult> UploadAndValidate(UploadFileMetadata f1, TargetDrive targetDrive)
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        var response1 = await client.DriveRedux.UploadNewMetadata(targetDrive, f1);
        Assert.IsTrue(response1.IsSuccessStatusCode);
        var getHeaderResponse1 = await client.DriveRedux.GetFileHeader(response1.Content!.File);
        Assert.IsTrue(getHeaderResponse1.IsSuccessStatusCode);
        return response1.Content;
    }
}