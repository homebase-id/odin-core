using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Membership.Connections;
using Odin.Core.Storage;

namespace Odin.Hosting.Tests.OwnerApi.Transit.Detection;

[TestFixture]
public class TransitBadCATDetectionTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
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
    public async Task CanDetectBadCAT_and_UpdateICR_and_FallbackToPublicAccess()
    {
        // Prepare Scenario

        // 1. Connect the hobbits
        var targetDrive = TargetDrive.NewTargetDrive();
        var scenarioCtx = await _scaffold.Scenarios.CreateConnectedHobbits(targetDrive);

        // 2. Merry posts two pieces of content 1 public, one secured that requires you to be connected
        // 

        var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);
        var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        var (publicFileUploadResult, publicPayloadContent) = await MerryPostPublicFile();
        var (securedFileUploadResult, securedPayloadContent) = await MerryPostSecureFileAndAuthorizePippin();

        //
        // 3. Pippin is connected so he can read both via transit query service
        //
        var getPublicPayloadTransitResponse1 =
            await pippinOwnerClient.Transit.GetPayloadOverTransit(merryOwnerClient.Identity.OdinId, publicFileUploadResult.File);
        Assert.That(getPublicPayloadTransitResponse1.IsSuccessStatusCode, Is.True);
        Assert.That(getPublicPayloadTransitResponse1.Content, Is.Not.Null);

        var remotePublicPayload1 = await getPublicPayloadTransitResponse1.Content.ReadAsStringAsync();
        ClassicAssert.IsTrue(remotePublicPayload1 == publicPayloadContent);

        var getSecuredPayloadTransitResponse1 =
            await pippinOwnerClient.Transit.GetPayloadOverTransit(merryOwnerClient.Identity.OdinId, securedFileUploadResult.File);
        Assert.That(getSecuredPayloadTransitResponse1.IsSuccessStatusCode, Is.True);
        Assert.That(getSecuredPayloadTransitResponse1.Content, Is.Not.Null);

        var remoteSecuredPayload1 = await getSecuredPayloadTransitResponse1.Content.ReadAsStringAsync();
        ClassicAssert.IsTrue(remoteSecuredPayload1 == securedPayloadContent);

        //
        // Merry gets mad and disconnects from Pippin, pippin leaves for Gondor with Gandalf 🧙‍🐎
        //
        await merryOwnerClient.Network.DisconnectFrom(pippinOwnerClient.Identity);

        //
        // Pippin makes transit query call to Merry still thinking they are connected (therefore he sends CAT).  
        //
        // On the backend - Merry's server detects bad CAT (or ICR), rejects the call and Tells Pippin's server that the CAT is invalid
        // therefore, the call should fail with 403
        // Pippin's server sees bad CAT then and updates Merry's ICR with a flag indicating not to use the CAT

        var getPublicPayloadTransitResponse2 =
            await pippinOwnerClient.Transit.GetPayloadOverTransit(merryOwnerClient.Identity.OdinId, publicFileUploadResult.File);

        Assert.That(getPublicPayloadTransitResponse2.IsSuccessStatusCode, Is.True);
        Assert.That(getPublicPayloadTransitResponse2.Content, Is.Not.Null);

        var remotePublicPayload2 = await getPublicPayloadTransitResponse2.Content.ReadAsStringAsync();
        ClassicAssert.IsTrue(remotePublicPayload2 == publicPayloadContent);


        // Call to secure should fail with 403
        var getSecurePayloadTransitResponse2 =
            await pippinOwnerClient.Transit.GetPayloadOverTransit(merryOwnerClient.Identity.OdinId, securedFileUploadResult.File);
        ClassicAssert.IsTrue(getSecurePayloadTransitResponse2.StatusCode == HttpStatusCode.Forbidden,
            $"Status code was {getSecurePayloadTransitResponse2.StatusCode}");

        //
        // Validate there is no longer a connection with merry/pippin
        //
        var merryConnectionOnPippin = await pippinOwnerClient.Network.GetConnectionInfo(TestIdentities.Merry);
        ClassicAssert.IsTrue(merryConnectionOnPippin.Status == ConnectionStatus.None);
    }

    private async Task<(UploadResult uploadResult, string securedPayloadContent)> MerryPostSecureFileAndAuthorizePippin()
    {
        var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);
        var merrySecuredDrive = TargetDrive.NewTargetDrive();
        await merryOwnerClient.Drive.CreateDrive(merrySecuredDrive, "a private blog", "", false, false, true);

        var canAccessSecureDriveCircle = await merryOwnerClient.Membership.CreateCircle("CanAccessSecureDrive", new PermissionSetGrantRequest()
        {
            PermissionSet = new PermissionSet(),
            Drives = new List<DriveGrantRequest>()
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = merrySecuredDrive,
                        Permission = DrivePermission.Read
                    }
                }
            }
        });

        await merryOwnerClient.Network.GrantCircle(canAccessSecureDriveCircle.Id, TestIdentities.Pippin);

        const string headerContent = "some secured header content";
        const string payloadContent = "this is the secured payload";

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            AppData = new UploadAppFileMetaData()
            {
                FileType = 10101,
                Content = headerContent
            },
            IsEncrypted = false,
            AccessControlList = AccessControlList.Connected
        };

        var publicFile = await merryOwnerClient.Drive.UploadFile(FileSystemType.Standard, merrySecuredDrive, fileMetadata, payloadContent,
            payloadKey: WebScaffold.PAYLOAD_KEY);
        return (publicFile, payloadContent);
    }

    private async Task<(UploadResult uploadResult, string securedPayloadContent)> MerryPostPublicFile()
    {
        var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);
        var merryPublicDrive = TargetDrive.NewTargetDrive();
        await merryOwnerClient.Drive.CreateDrive(merryPublicDrive, "a public blog", "", true, false, true);

        const string headerContent = "some public header content";
        const string payloadContent = "this is the public payload";

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            AppData = new UploadAppFileMetaData()
            {
                FileType = 10101,
                Content = headerContent
            },
            IsEncrypted = false,
            AccessControlList = AccessControlList.Anonymous
        };

        var publicFile = await merryOwnerClient.Drive.UploadFile(FileSystemType.Standard, merryPublicDrive, fileMetadata, payloadData: payloadContent,
            payloadKey: WebScaffold.PAYLOAD_KEY);
        return (publicFile, payloadContent);
    }
}