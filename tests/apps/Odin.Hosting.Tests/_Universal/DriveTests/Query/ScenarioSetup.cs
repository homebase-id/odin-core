using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._Universal.DriveTests.Query;

public static class Scenario
{
    public static async Task<(TargetDrive TargetDrive, int FileType, UploadResult UnencryptedFileUploadResult, UploadResult EncryptedFileUploadResult)> ConfigureScenario1(WebScaffold scaffold)
    {
        var frodoOwnerClient = scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var samOwnerClient = scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var pippinOwnerClient = scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);

        var targetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await frodoOwnerClient.DriveManager.CreateDrive(targetDrive, "Public posts drive", "", allowAnonymousReads: true);

        var mordorCrewCircle = Guid.NewGuid();
        await frodoOwnerClient.Network.CreateCircle(mordorCrewCircle, "Mordor Crew", new PermissionSetGrantRequest()
        {
            Drives =
            [
                new DriveGrantRequest()
                {
                    PermissionedDrive = new() { Drive = targetDrive, Permission = DrivePermission.Read },
                },
            ]
        });

        var hobbitsCircle = Guid.NewGuid();
        await frodoOwnerClient.Network.CreateCircle(hobbitsCircle, "Hobbits", new PermissionSetGrantRequest()
        {
            Drives =
            [
                new DriveGrantRequest()
                {
                    PermissionedDrive = new() { Drive = targetDrive, Permission = DrivePermission.Read },
                },
            ]
        });

        // Connect frodo and sam; giving sam hobbits and mordor crew circle
        await frodoOwnerClient.Connections.SendConnectionRequest(TestIdentities.Samwise.OdinId, new List<GuidId>() { hobbitsCircle, mordorCrewCircle });
        await samOwnerClient.Connections.AcceptConnectionRequest(TestIdentities.Frodo.OdinId);

        // Connect merry and pippin; no circles
        await frodoOwnerClient.Connections.SendConnectionRequest(TestIdentities.Pippin.OdinId, new List<GuidId>() { hobbitsCircle });
        await pippinOwnerClient.Connections.AcceptConnectionRequest(TestIdentities.Frodo.OdinId);

        // frodo uploads a post
        const int fileType = 1090;
        var file1 = SampleMetadataData.CreateWithContent(fileType: fileType,
            content:"a bit of content",
            acl: new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.ConfirmConnected, CircleIdList = [mordorCrewCircle] });
        var file1UploadResult = await UploadUnencryptedFileAndValidate(scaffold, file1, targetDrive);

        var file1EncryptedUploadResult = await UploadEncryptedFileAndValidate(scaffold, file1, targetDrive);
        // file 2: only connected identities can see it (a circle is not required)
        // var file2 = SampleMetadataData.Create(fileType: fileType, acl: new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected });
        // var file2UploadResult = await UploadAndValidate(file2, targetDrive);

        return (targetDrive, fileType, file1UploadResult, file1EncryptedUploadResult);
    }

    private static async Task<UploadResult> UploadUnencryptedFileAndValidate(WebScaffold scaffold, UploadFileMetadata f1, TargetDrive targetDrive)
    {
        var client = scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var response1 = await client.DriveRedux.UploadNewMetadata(targetDrive, f1);
        Assert.IsTrue(response1.IsSuccessStatusCode);
        var getHeaderResponse1 = await client.DriveRedux.GetFileHeader(response1.Content!.File);
        Assert.IsTrue(getHeaderResponse1.IsSuccessStatusCode);
        return response1.Content;
    }

    private static async Task<UploadResult> UploadEncryptedFileAndValidate(WebScaffold scaffold, UploadFileMetadata f1, TargetDrive targetDrive)
    {
        var client = scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var (response, encryptedJsonContent64) = await client.DriveRedux.UploadNewEncryptedMetadata(targetDrive, f1);
        Assert.IsTrue(response.IsSuccessStatusCode);
        var getHeaderResponse1 = await client.DriveRedux.GetFileHeader(response.Content!.File);
        Assert.IsTrue(getHeaderResponse1.IsSuccessStatusCode);
        return response.Content;
    }
}