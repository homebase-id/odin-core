using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveQuery;

/// <summary>
/// The fast-framework twin of <c>_Universal/DriveTests/Query/ScenarioSetup</c>'s
/// <c>Scenario.ConfigureScenario1</c>, used by <see cref="AppQueryTests"/> and
/// <see cref="PeerQueryTests"/>.
/// </summary>
/// <remarks>
/// The V1 original takes a <c>WebScaffold</c> and creates its own clients from it; that shape cannot
/// follow here, so this takes the already-logged-in owner sessions instead. The scenario it builds is
/// identical: Frodo owns an anonymous-readable channel drive; two circles (Mordor Crew and Hobbits)
/// each grant Read on it; Sam is connected and holds both circles, Pippin is connected and holds only
/// Hobbits; and Frodo posts the same metadata twice — once unencrypted, once encrypted — with an ACL
/// restricted to Connected members of the Mordor Crew circle.
///
/// The V1 <c>ScenarioSetup.cs</c> stays where it is: it is not part of this port's deletions.
/// </remarks>
public static class QueryScenario
{
    public sealed record ScenarioConfig(
        TargetDrive TargetDrive,
        int FileType,
        UploadResult UnencryptedFileUploadResult,
        UploadResult EncryptedFileUploadResult);

    public static async Task<ScenarioConfig> ConfigureScenario1Async(OwnerSession frodo, OwnerSession sam, OwnerSession pippin)
    {
        var targetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await frodo.Admin.CreateDrive(targetDrive, "Public posts drive", allowAnonymousReads: true);

        var mordorCrewCircle = Guid.NewGuid();
        await frodo.Admin.CreateCircle(mordorCrewCircle, "Mordor Crew", new PermissionSetGrantRequest()
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
        await frodo.Admin.CreateCircle(hobbitsCircle, "Hobbits", new PermissionSetGrantRequest()
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
        await frodo.Connections.SendConnectionRequest(sam.Identity, new List<GuidId>() { hobbitsCircle, mordorCrewCircle });
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);

        // Connect merry and pippin; no circles
        await frodo.Connections.SendConnectionRequest(pippin.Identity, new List<GuidId>() { hobbitsCircle });
        await pippin.Connections.AcceptConnectionRequest(frodo.Identity);

        // frodo uploads a post
        const int fileType = 1090;
        var file1 = SampleMetadataData.CreateWithContent(fileType: fileType,
            content: "a bit of content",
            acl: new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected, CircleIdList = [mordorCrewCircle] });
        var file1UploadResult = await UploadUnencryptedFileAndValidate(frodo, file1, targetDrive);

        var file1EncryptedUploadResult = await UploadEncryptedFileAndValidate(frodo, file1, targetDrive);
        // file 2: only connected identities can see it (a circle is not required)
        // var file2 = SampleMetadataData.Create(fileType: fileType, acl: new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected });
        // var file2UploadResult = await UploadAndValidate(file2, targetDrive);

        return new ScenarioConfig(targetDrive, fileType, file1UploadResult, file1EncryptedUploadResult);
    }

    private static async Task<UploadResult> UploadUnencryptedFileAndValidate(OwnerSession frodo,
        UploadFileMetadata f1, TargetDrive targetDrive)
    {
        var response1 = await frodo.V1.Drive.UploadNewMetadata(targetDrive, f1);
        Assert.That(response1.IsSuccessStatusCode, Is.True);
        var getHeaderResponse1 = await frodo.V1.Drive.GetFileHeader(response1.Content!.File);
        Assert.That(getHeaderResponse1.IsSuccessStatusCode, Is.True);
        return response1.Content;
    }

    private static async Task<UploadResult> UploadEncryptedFileAndValidate(OwnerSession frodo,
        UploadFileMetadata f1, TargetDrive targetDrive)
    {
        var (response, _) = await frodo.V1.Drive.UploadNewEncryptedMetadata(targetDrive, f1);
        Assert.That(response.IsSuccessStatusCode, Is.True);
        var getHeaderResponse1 = await frodo.V1.Drive.GetFileHeader(response.Content!.File);
        Assert.That(getHeaderResponse1.IsSuccessStatusCode, Is.True);
        return response.Content;
    }
}
