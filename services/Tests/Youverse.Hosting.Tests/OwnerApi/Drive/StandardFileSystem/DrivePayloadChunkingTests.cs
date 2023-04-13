using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Base;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Storage;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;

namespace Youverse.Hosting.Tests.OwnerApi.Drive.StandardFileSystem;

public class DrivePayloadChunkingTests
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
        var uploadedPayload = "What is happening with the encoding!?";

        var uploadedContentResult = await UploadStandardFileToChannel(ownerClient, frodoChannelDrive, uploadedContent, uploadedPayload);

        //Test whole payload is there
        var getPayloadResponse = await ownerClient.Drive.GetPayload(FileSystemType.Standard, uploadedContentResult.File);
        string payloadContent = await getPayloadResponse.ReadAsStringAsync();
        Assert.IsTrue(payloadContent == uploadedPayload);

        // const string expectedChunk = "I knew I’d want it";
        const string expectedChunk = "is happening";

        //get a chunk of the payload
        var chunk1 = new FileChunk()
        {
            Start = 5,
            Length = expectedChunk.Length
        };

        var getPayloadResponseChunk1 = await ownerClient.Drive.GetPayload(FileSystemType.Standard, uploadedContentResult.File, chunk1);
        string payloadContentChunk1 = await getPayloadResponseChunk1.ReadAsStringAsync();
        Assert.IsTrue(payloadContentChunk1 == expectedChunk,$"expected [{expectedChunk}] but value was [{payloadContentChunk1}]");
        Assert.IsTrue(chunk1.Length == payloadContentChunk1.Length);
    }

    private async Task<UploadResult> UploadStandardFileToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent, string payload)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = string.IsNullOrEmpty(payload),
                JsonContent = uploadedContent,
                FileType = 200,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata, payload);
    }
}