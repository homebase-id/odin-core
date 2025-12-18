using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._V2.Tests.Drive.DriveReaderTests;

public class GetTransferHistoryTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: [TestIdentities.Frodo, TestIdentities.Samwise]);
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

    public static IEnumerable TestCasesSecuredDrive()
    {
        yield return new object[] { new GuestTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.Forbidden };
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.OK };

        yield return new object[] { new GuestTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.Forbidden };
        yield return new object[] { new AppTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.Forbidden };

        yield return new object[] { new CdnTestCase(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.Unauthorized };
        yield return new object[] { new OwnerTestCase(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(TestCasesSecuredDrive))]
    public async Task GetTransferHistory(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Authenticated;
        metadata.AllowDistribution = true;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        List<TestIdentity> recipients = [TestIdentities.Frodo];
        var uploadResult = await TransferFile(identity, metadata, payload, recipients, callerContext);

        await callerContext.Initialize(ownerApiClient);
        var client = new DriveReaderV2Client(identity.OdinId, callerContext.GetFactory());

        //
        // can get transfer history
        //
        var file = uploadResult.File;
        var getTransferHistory = await client.GetTransferHistoryAsync(file.TargetDrive.Alias, file.FileId);
        ClassicAssert.IsTrue(getTransferHistory.StatusCode == expectedStatusCode, $"code was {getTransferHistory.StatusCode}");

        // test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var transferHistory = getTransferHistory.Content;
            ClassicAssert.IsNotNull(transferHistory);

            // see that each recipient has a transfer history item
            foreach (var recipient in recipients)
            {
                var item = transferHistory.GetHistoryItem(recipient.OdinId);
                ClassicAssert.IsNotNull(item);
                ClassicAssert.IsTrue(item.LatestTransferStatus == LatestTransferStatus.Delivered,
                    $"actual status was {item.LatestTransferStatus}");
            }
        }

        await Cleanup(identity, recipients);
    }

    public async Task Cleanup(TestIdentity identity, List<TestIdentity> recipients)
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        foreach (var recipient in recipients)
        {
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);
            await ownerApiClient.Connections.DisconnectFrom(recipient.OdinId);
            await recipientOwnerClient.Connections.DisconnectFrom(ownerApiClient.OdinId);
        }
    }

    private async Task<UploadResult> TransferFile(TestIdentity identity, UploadFileMetadata uploadedFileMetadata,
        TestPayloadDefinition payloadDefinition,
        List<TestIdentity> recipients,
        IApiClientContext callerContext)
    {
        var targetDrive = callerContext.TargetDrive;

        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", false);

        foreach (var recipient in recipients)
        {
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);
            await recipientOwnerClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", false);

            var cid = Guid.NewGuid();
            var permissions = TestUtils.CreatePermissionGrantRequest(targetDrive, DrivePermission.Write);
            await recipientOwnerClient.Network.CreateCircle(cid, "circle with some access", permissions);

            await ownerApiClient.Connections.SendConnectionRequest(recipient.OdinId);
            await recipientOwnerClient.Connections.AcceptConnectionRequest(ownerApiClient.OdinId, [cid]);
        }

        // upload file
        var testPayloads = new List<TestPayloadDefinition> { payloadDefinition };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var transitOptions = new TransitOptions
        {
            IsTransient = false,
            Recipients = recipients.Select(t => t.OdinId.ToString()).ToList(),
            DisableTransferHistory = false,
            Priority = OutboxPriority.High
        };

        var response = await ownerApiClient.DriveRedux.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads,
            transitOptions);

        //
        // wait for all file movement to be done
        //
        await ownerApiClient.DriveRedux.WaitForEmptyOutbox(targetDrive);

        foreach (var recipient in recipients)
        {
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);
            await recipientOwnerClient.DriveRedux.ProcessInbox(callerContext.TargetDrive);
            await recipientOwnerClient.DriveRedux.WaitForEmptyInbox(callerContext.TargetDrive);
        }

        // send back details
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        ClassicAssert.IsNotNull(uploadResult);

        return uploadResult;
    }
}