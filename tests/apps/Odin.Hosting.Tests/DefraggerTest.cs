using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Util;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._Universal.DriveTests.Inbox;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Tests;

public class DefraggerTest
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(initializeIdentity: true);
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
        _scaffold.DumpLogEventsToConsole();
        _scaffold.AssertLogEvents();
    }


    [Test]
    [Explicit]
    public async Task DefragDriveTest()
    {
        var ownerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        // Upload two files
        var targetDrive = TargetDrive.NewTargetDrive();
        await UploadFile(targetDrive, TestIdentities.Samwise);


        // Place some files in Sam's inbox
        var targetDrive2 = TargetDrive.NewTargetDrive();
        var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        const DrivePermission drivePermissions = DrivePermission.Write;
        const int totalFileCount = 32;

        await PrepareScenario(senderOwnerClient, ownerClient, targetDrive2, drivePermissions);
        var fileSendResults = await SendFiles(senderOwnerClient, ownerClient, targetDrive2, totalFileCount);
        ClassicAssert.IsTrue(fileSendResults.Count == totalFileCount);


        var t = await ownerClient.DriveManager.GetDrives();
        var drives = t.Content.Results;

        foreach (var drive in drives)
        {
            // this calls to the server and on the server side you will perform the defrag
            // doing it this way ensures all context and all services are setup correclty
            await ownerClient.DriveManager.Defrag(drive.TargetDriveInfo);
        }
    }

    private async Task UploadFile(TargetDrive targetDrive, TestIdentity identity)
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var testPayloads = new List<TestPayloadDefinition>()
        {
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(),
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2()
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var callerDriveClient = ownerApiClient.DriveRedux;
        var response = await callerDriveClient.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);

        var uploadResult = response.Content;
        ClassicAssert.IsNotNull(uploadResult);

        // use the owner api client to validate the file that was uploaded
        var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        ClassicAssert.IsNotNull(header);
        ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == uploadedFileMetadata.AppData.Content);
        ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == testPayloads.Count);

        // 
        // verify payloads are in place
        //
        foreach (var definition in testPayloads)
        {
            //test the headers payload info
            var payload = header.FileMetadata.Payloads.Single(p => p.Key == definition.Key);
            ClassicAssert.IsTrue(definition.Thumbnails.Count == payload.Thumbnails.Count);
            ClassicAssert.IsTrue(definition.ContentType == payload.ContentType);
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(definition.Iv, payload.Iv));

            var getPayloadResponse = await ownerApiClient.DriveRedux.GetPayload(uploadResult.File, definition.Key);
            ClassicAssert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getPayloadResponse.ContentHeaders!.LastModified.HasValue);
            ClassicAssert.IsTrue(getPayloadResponse.ContentHeaders.LastModified.GetValueOrDefault() <
                                 DateTimeOffset.Now.AddSeconds(10));

            var content = (await getPayloadResponse.Content.ReadAsStreamAsync()).ToByteArray();
            CollectionAssert.AreEqual(content, definition.Content);

            // Check all the thumbnails
            foreach (var thumbnail in definition.Thumbnails)
            {
                var getThumbnailResponse = await ownerApiClient.DriveRedux.GetThumbnail(uploadResult.File,
                    thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);

                ClassicAssert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                ClassicAssert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
                ClassicAssert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() <
                                     DateTimeOffset.Now.AddSeconds(10));

                var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
                CollectionAssert.AreEqual(thumbContent, thumbnail.Content);
            }
        }
    }


    private async Task<List<FileSendResponse>> SendFiles(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient,
        TargetDrive targetDrive, int totalFiles)
    {
        var results = new List<FileSendResponse>();

        for (var i = 0; i < totalFiles; i++)
        {
            var fileContent = $"some string {i}";
            var (uploadResult, encryptedJsonContent64) =
                await SendStandardFile(senderOwnerClient, targetDrive, fileContent, recipientOwnerClient.Identity);

            ClassicAssert.IsTrue(uploadResult.RecipientStatus.TryGetValue(recipientOwnerClient.Identity.OdinId, out var recipientStatus));
            ClassicAssert.IsTrue(recipientStatus == TransferStatus.Enqueued,
                $"Should have been delivered, actual status was {recipientStatus}");

            results.Add(new FileSendResponse()
            {
                UploadResult = uploadResult,
                DecryptedContent = fileContent,
                EncryptedContent64 = encryptedJsonContent64
            });
        }

        await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(targetDrive);
        return results;
    }

    /// <summary>
    /// Sends a standard file to a single recipient and performs basic assertions required by all tests
    /// </summary>
    private async Task<(UploadResult, string encryptedJsonContent64)> SendStandardFile(OwnerApiClientRedux sender, TargetDrive targetDrive,
        string uploadedContent, TestIdentity recipient)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = default,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.Connected
        };

        var storageOptions = new StorageOptions()
        {
            Drive = targetDrive
        };

        var transitOptions = new TransitOptions()
        {
            Recipients = [recipient.OdinId],
            RemoteTargetDrive = default
        };

        var (uploadResponse, encryptedJsonContent64) = await sender.DriveRedux.UploadNewEncryptedMetadata(
            fileMetadata,
            storageOptions,
            transitOptions
        );

        var uploadResult = uploadResponse.Content;

        //
        // Basic tests first which apply to all calls
        //
        ClassicAssert.IsTrue(uploadResult.RecipientStatus.Count == 1);

        return (uploadResult, encryptedJsonContent64);
    }

    private async Task PrepareScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient,
        TargetDrive targetDrive,
        DrivePermission drivePermissions)
    {
        //
        // Recipient creates a target drive
        //
        var recipientDriveResponse = await recipientOwnerClient.DriveManager.CreateDrive(
            targetDrive: targetDrive,
            name: "Target drive on recipient",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);

        ClassicAssert.IsTrue(recipientDriveResponse.IsSuccessStatusCode);

        //
        // Sender needs this same drive in order to send across files
        //
        var senderDriveResponse = await senderOwnerClient.DriveManager.CreateDrive(
            targetDrive: targetDrive,
            name: "Target drive on sender",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);

        ClassicAssert.IsTrue(senderDriveResponse.IsSuccessStatusCode);

        //
        // Recipient creates a circle with target drive, read and write access
        //
        var expectedPermissionedDrive = new PermissionedDrive()
        {
            Drive = targetDrive,
            Permission = drivePermissions
        };

        var circleId = Guid.NewGuid();
        var createCircleResponse = await recipientOwnerClient.Network.CreateCircle(circleId, "Circle with drive access",
            new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = expectedPermissionedDrive
                    }
                }
            });

        ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode);

        //
        // Sender sends connection request
        //
        await senderOwnerClient.Connections.SendConnectionRequest(recipientOwnerClient.Identity.OdinId, new List<GuidId>() { });

        //
        // Recipient accepts; grants access to circle
        //
        await recipientOwnerClient.Connections.AcceptConnectionRequest(senderOwnerClient.Identity.OdinId, new List<GuidId>() { circleId });

        // 
        // Test: At this point: recipient should have an ICR record on sender's identity that does not have a key
        // 

        var getConnectionInfoResponse = await recipientOwnerClient.Network.GetConnectionInfo(senderOwnerClient.Identity.OdinId);

        ClassicAssert.IsTrue(getConnectionInfoResponse.IsSuccessStatusCode);
        var senderConnectionInfo = getConnectionInfoResponse.Content;

        ClassicAssert.IsNotNull(senderConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
            cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive)));
    }

    private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient)
    {
        await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
    }
}