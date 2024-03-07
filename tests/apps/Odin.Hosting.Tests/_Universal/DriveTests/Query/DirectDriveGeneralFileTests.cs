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
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.ApiClient.Transit.Query.Query;

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
        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var pippinOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var merryOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

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
        var file1 = SampleMetadataData.Create(fileType: fileType,
            acl: new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected, CircleIdList = [mordorCrewCircle] });
        var file1UploadResult = await UploadAndValidate(file1, targetDrive);

        // file 2: only connected identities can see it (a circle is not required)
        // var file2 = SampleMetadataData.Create(fileType: fileType, acl: new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected });
        // var file2UploadResult = await UploadAndValidate(file2, targetDrive);

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

        var peerQueryBatchReqeust = new PeerQueryBatchRequest()
        {
            OdinId = TestIdentities.Frodo.OdinId,
            QueryParams = query.QueryParams,
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };

        // Assert
        // Sam queries frodo over peer (as he would from his owner feed)
        var samOwnerClientQueryResponse = await samOwnerClient.PeerQuery.GetBatch(peerQueryBatchReqeust);
        Assert.IsTrue(samOwnerClientQueryResponse.IsSuccessStatusCode);
        Assert.IsTrue(samOwnerClientQueryResponse.Content.SearchResults.Count() == 1);
        
        // Pippin queries frodo over peer (as he would from his owner feed)
        var pippinOwnerClientQueryResponse = await pippinOwnerClient.PeerQuery.GetBatch(peerQueryBatchReqeust);
        Assert.IsTrue(pippinOwnerClientQueryResponse.IsSuccessStatusCode);
        Assert.IsTrue(pippinOwnerClientQueryResponse.Content.SearchResults.Count() == 0);
        
        // Merry queries frodo over peer (as he would from his owner feed)
        var merryOwnerClientQueryResponse = await merryOwnerClient.PeerQuery.GetBatch(peerQueryBatchReqeust);
        Assert.IsTrue(merryOwnerClientQueryResponse.IsSuccessStatusCode);
        Assert.IsTrue(merryOwnerClientQueryResponse.Content.SearchResults.Count() == 0);
        
        //
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
    }

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

    private async Task<UploadResult> UploadAndValidate(UploadFileMetadata f1, TargetDrive targetDrive)
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var response1 = await client.DriveRedux.UploadNewMetadata(targetDrive, f1);
        Assert.IsTrue(response1.IsSuccessStatusCode);
        var getHeaderResponse1 = await client.DriveRedux.GetFileHeader(response1.Content!.File);
        Assert.IsTrue(getHeaderResponse1.IsSuccessStatusCode);
        return response1.Content;
    }
}