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
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
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

        var targetDrive = TargetDrive.NewTargetDrive();
        await UploadFile(targetDrive, TestIdentities.Samwise);


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
}