using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;

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
        
        var targetDrive = callerContext.TargetDrive;
        await pippinOwnerClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

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

        // Pippin uploads files
        // file 1: only connect identities in a specific circle 
        var file1 = SampleMetadataData.Create(fileType: 100,
            acl: new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected, CircleIdList = [circle1] });
        var file1UploadResult = await UploadAndValidate(file1, targetDrive);

        // file 2: only connected identities can see it (a circle is not required)
        var file2 = SampleMetadataData.Create(fileType: 100, acl: new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected });
        var file2UploadResult = await UploadAndValidate(file2, targetDrive);

        // Act
        // query Pippin's drive as Sam; should get both files

        // query Pippin's drive as Merry; should get file2 only

        //
        await callerContext.Initialize(pippinOwnerClient);

        // Act
        var callerDriveClient = new UniversalDriveApiClient(TestIdentities.Pippin.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UploadNewMetadata(targetDrive, file1);

        // Assert
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");
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