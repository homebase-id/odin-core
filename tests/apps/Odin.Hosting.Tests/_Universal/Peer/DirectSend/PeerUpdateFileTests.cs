using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Direct;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.Peer.DirectSend;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class PeerUpdateFileTests
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

    public static IEnumerable OwnerAllowed()
    {
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable AppAllowed()
    {
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable GuestAllowed()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task CanUpdateFileUpdateHeaderDeletePayloadAndAddNewPayload(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var sender = senderOwnerClient.Identity.OdinId;
        var recipient = recipientOwnerClient.Identity.OdinId;

        var targetDrive = callerContext.TargetDrive;
        await recipientOwnerClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var cid = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(callerContext.TargetDrive, DrivePermission.Write);
        await recipientOwnerClient.Network.CreateCircle(cid, "circle with some access", permissions);

        await senderOwnerClient.Connections.SendConnectionRequest(recipient);
        await recipientOwnerClient.Connections.AcceptConnectionRequest(sender, [cid]);


        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AllowDistribution = true;
        var payload1 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var payload2 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2();

        var testPayloads = new List<TestPayloadDefinition>()
        {
            payload1,
            payload2
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        //Pippin sends a file to the recipient
        var response = await senderOwnerClient.PeerDirect.TransferNewFile(targetDrive, uploadedFileMetadata, [recipient], null, uploadManifest, testPayloads);
        Assert.IsTrue(response.IsSuccessStatusCode);

        //
        // Update the file via pippin's identity
        //
        await callerContext.Initialize(senderOwnerClient);
        var callerDriveClient = new UniversalPeerDirectApiClient(sender, callerContext.GetFactory());

        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 2900;

        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition1();
        var updateInstructionSet = new PeerUpdateInstructionSet
        {
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = response.Content.RemoteGlobalTransitIdFileIdentifier,
            Recipient = recipient,

            UpdateOperations =
            [
                new FileUpdateOperation
                {
                    FileUpdateOperationType = FileUpdateOperationType.UpdateManifest
                },
                new FileUpdateOperation
                {
                    FileUpdateOperationType = FileUpdateOperationType.DeletePayload,
                    PayloadKey = payload1.Key
                },
                new FileUpdateOperation
                {
                    FileUpdateOperationType = FileUpdateOperationType.AddPayload,
                    PayloadKey = payloadToAdd.Key
                }
            ],

            Manifest = new UploadManifest
            {
                PayloadDescriptors =
                [
                    new UploadManifestPayloadDescriptor
                    {
                        Iv = ByteArrayUtil.GetRndByteArray(16),
                        PayloadKey = payloadToAdd.Key,
                        DescriptorContent = null,
                        ContentType = payloadToAdd.ContentType,
                        PreviewThumbnail = default,
                        Thumbnails = new List<UploadedManifestThumbnailDescriptor>()
                    }
                ]
            }
        };

        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, testPayloads);
        Assert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var uploadResult = updateFileResponse.Content;
            Assert.IsNotNull(uploadResult);

            var gtid = uploadResult.RemoteGlobalTransitIdFileIdentifier;

            //
            // Recipient should have the updated file
            //
            var getHeaderResponse = await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(gtid);
            Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content.SearchResults.SingleOrDefault();
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
            Assert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            Assert.IsTrue(header.FileMetadata.Payloads.Count() == 2);
            Assert.IsTrue(header.FileMetadata.Payloads.All(pd => pd.Key != payload1.Key), "payload 1 should have been removed");
            Assert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == payload2.Key), "payload 2 should remain");
            Assert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == payloadToAdd.Key), "payloadToAdd should have been, well, added :)");

            var file = new ExternalFileIdentifier()
            {
                FileId = header.FileId,
                TargetDrive = header.TargetDrive
            };

            //
            // Ensure payloadToAdd add is added
            //
            var getPayloadToAddResponse = await recipientOwnerClient.DriveRedux.GetPayload(file, payloadToAdd.Key);
            Assert.IsTrue(getPayloadToAddResponse.IsSuccessStatusCode);
            Assert.IsTrue(getPayloadToAddResponse.ContentHeaders!.LastModified.HasValue);
            Assert.IsTrue(getPayloadToAddResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

            var content = (await getPayloadToAddResponse.Content.ReadAsStreamAsync()).ToByteArray();
            CollectionAssert.AreEqual(content, payloadToAdd.Content);

            // Check all the thumbnails
            foreach (var thumbnail in payloadToAdd.Thumbnails)
            {
                var getThumbnailResponse = await recipientOwnerClient.DriveRedux.GetThumbnail(file,
                    thumbnail.PixelWidth, thumbnail.PixelHeight, payloadToAdd.Key);

                Assert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                Assert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
                Assert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

                var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
                CollectionAssert.AreEqual(thumbContent, thumbnail.Content);
            }

            //
            // Ensure we get payload2 for the payload1
            //
            var getPayload2Response = await recipientOwnerClient.DriveRedux.GetPayload(file, payloadToAdd.Key);
            Assert.IsTrue(getPayload2Response.IsSuccessStatusCode);

            //
            // Ensure we get 404 for the payload1
            //
            var getPayload1Response = await recipientOwnerClient.DriveRedux.GetPayload(file, payloadToAdd.Key);
            Assert.IsTrue(getPayload1Response.StatusCode == HttpStatusCode.NotFound);
        }
    }
}