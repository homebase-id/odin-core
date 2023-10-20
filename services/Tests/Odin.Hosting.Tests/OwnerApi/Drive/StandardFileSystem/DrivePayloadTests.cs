using System;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests.OwnerApi.Drive.StandardFileSystem;

public class DrivePayloadTests
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
    public async Task CanDeletePayload()
    {
        var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await ownerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", false, false);

        // Frodo uploads content to channel drive
        const string uploadedContent = "I'm Mr. Underhill";
        var payload = new TestPayload()
        {
            Key = "ppp",
            Data = "What is happening with the encoding!?"
        };

        var uploadedContentResult = await UploadStandardFileToChannel(ownerClient, frodoChannelDrive, uploadedContent, payload);

        //Test whole payload is there
        var getPayloadResponse = await ownerClient.Drive.GetPayload(FileSystemType.Standard, uploadedContentResult.File, payload.Key);
        string payloadContent = await getPayloadResponse.ReadAsStringAsync();
        Assert.IsTrue(payloadContent == payload.Data);

        var deleteResult = await ownerClient.Drive.DeletePayload(FileSystemType.Standard, uploadedContentResult.File, payload.Key);
        Assert.IsTrue(deleteResult.NewVersionTag != uploadedContentResult.NewVersionTag);

        //validate the payload is gone
        var getDeletedPayloadResponse = await ownerClient.Drive.GetPayloadRaw(FileSystemType.Standard, uploadedContentResult.File, payload.Key);
        Assert.IsTrue(getDeletedPayloadResponse.StatusCode == HttpStatusCode.NotFound);

        //even tho the payload is gone, we should still be able to get the header and it should be updated
        var getHeaderResponse = await ownerClient.Drive.GetFileHeaderRaw(FileSystemType.Standard, uploadedContentResult.File);
        Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        Assert.IsTrue(getHeaderResponse.Content.FileMetadata.AppData.ContentIsComplete);
    }

    [Test]
    public async Task CanGetPayloadInChunks()
    {
        var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await ownerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", false, false);

        // Frodo uploads content to channel drive
        const string uploadedContent = "I'm Mr. Underhill";

        // var uploadedPayload = "‘Rope!’ muttered Sam. ‘I knew I’d want it, if I hadn’t got it!’";
        var payload = new TestPayload()
        {
            Key = "ppp",
            Data = "What is happening with the encoding!?"
        };

        var uploadedContentResult = await UploadStandardFileToChannel(ownerClient, frodoChannelDrive, uploadedContent, payload);

        //Test whole payload is there
        var getPayloadResponse = await ownerClient.Drive.GetPayload(FileSystemType.Standard, uploadedContentResult.File, payload.Key);
        string payloadContent = await getPayloadResponse.ReadAsStringAsync();
        Assert.IsTrue(payloadContent == payload.Data);

        // const string expectedChunk = "I knew I’d want it";
        // const string expectedChunk = "is happening";
        const string expectedChunk = "encoding!?";

        //get a chunk of the payload
        var chunk1 = new FileChunk()
        {
            Start = 27,
            Length = expectedChunk.Length + 10
        };

        var getPayloadResponseChunk1 = await ownerClient.Drive.GetPayload(FileSystemType.Standard, uploadedContentResult.File, payload.Key, chunk1);
        string payloadContentChunk1 = await getPayloadResponseChunk1.ReadAsStringAsync();
        Assert.IsTrue(payloadContentChunk1 == expectedChunk, $"expected [{expectedChunk}] but value was [{payloadContentChunk1}]");
    }

    [Test]
    [Ignore("for testing encoding")]
    public async Task CanGetPayloadInChunks_Weird()
    {
        var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await ownerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", false, false);

        // Frodo uploads content to channel drive
        const string uploadedContent = "I'm Mr. Underhill";

        var payload = new TestPayload()
        {
            Key = "ppp",
            Data = "‘Rope!’ muttered Sam. ‘I knew I’d want it, if I hadn’t got it!’"
        };

        var uploadedContentResult = await UploadStandardFileToChannel(ownerClient, frodoChannelDrive, uploadedContent, payload);

        //Test whole payload is there
        var getPayloadResponse = await ownerClient.Drive.GetPayload(FileSystemType.Standard, uploadedContentResult.File, payload.Key);
        string payloadContent = await getPayloadResponse.ReadAsStringAsync();
        Assert.IsTrue(payloadContent == payload.Data);

        const string expectedChunk = "I knew I’d want it";
        // const string expectedChunk = "is happening";
        // const string expectedChunk = "encoding!?";

        //get a chunk of the payload
        var chunk1 = new FileChunk()
        {
            Start = 23,
            Length = expectedChunk.Length
        };

        var getPayloadResponseChunk1 = await ownerClient.Drive.GetPayload(FileSystemType.Standard, uploadedContentResult.File, payload.Key, chunk1);
        string payloadContentChunk1 = await getPayloadResponseChunk1.ReadAsStringAsync();
        Assert.IsTrue(payloadContentChunk1 == expectedChunk, $"expected [{expectedChunk}] but value was [{payloadContentChunk1}]");
    }

    private async Task<UploadResult> UploadStandardFileToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent, TestPayload payload)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = false,
                JsonContent = uploadedContent,
                FileType = 200,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        return await client.Drive.UploadNewFile(targetDrive, fileMetadata, payload);
    }
}