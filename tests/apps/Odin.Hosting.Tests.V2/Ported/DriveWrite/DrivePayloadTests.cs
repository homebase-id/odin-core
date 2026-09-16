using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>OwnerApi/Drive/StandardFileSystem/DrivePayloadTests</c>. Payload reads and deletes on
/// a channel drive as the owner: deleting a payload bumps the version tag, removes the payload from
/// disk and from the header while leaving the file active; and a byte-range read returns exactly the
/// requested chunk.
/// </summary>
/// <remarks>
/// No caller matrix — an <c>OwnerApi</c> fixture, so plain <c>[Test]</c> methods and
/// <c>LoginAsOwner</c>; the <c>SetupCallerWithOwner</c> ordering caveat does not apply. The V1
/// <c>OwnerApiClient.Drive</c> calls become <c>owner.V1.Drive</c> against the same V1 endpoints.
/// The upload manifest is spelled out so the request matches what
/// <c>DriveApiClient.UploadNewFile</c> sent (one payload, null descriptor <c>Iv</c>, stream part
/// content type <c>application/x-binary</c>). Drive creation moves to <c>owner.Admin.CreateDrive</c>,
/// whose <c>Metadata</c> default (<c>string.Empty</c>) matches the original's <c>""</c>; no
/// assertion reads the drive's name or metadata.
///
/// <c>CanGetPayloadInChunks_Weird</c> keeps its <c>[Ignore("for testing encoding")]</c> verbatim.
/// </remarks>
[TestFixture]
public class DrivePayloadTests : V2Fixture
{
    [Test]
    public async Task CanDeletePayload()
    {
        var ownerClient = await LoginAsOwner();

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await ownerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false);

        // Frodo uploads content to channel drive
        const string uploadedContent = "I'm Mr. Underhill";
        var payload = new TestPayload()
        {
            Key = "ppppeeeer",
            Data = "What is happening with the encoding!?"
        };

        var uploadedContentResult = await UploadStandardFileToChannel(ownerClient, frodoChannelDrive, uploadedContent, payload);

        //Test whole payload is there
        var getPayloadResponse = await ownerClient.V1.Drive.GetPayload(uploadedContentResult.File, payload.Key);
        string payloadContent = await getPayloadResponse.Content!.ReadAsStringAsync();
        Assert.That(payloadContent, Is.EqualTo(payload.Data));

        var deleteResponse = await ownerClient.V1.Drive.DeletePayload(uploadedContentResult.File,
            uploadedContentResult.NewVersionTag, payload.Key);
        Assert.That(deleteResponse.IsSuccessStatusCode, Is.True);
        Assert.That(deleteResponse.Content!.NewVersionTag, Is.Not.EqualTo(uploadedContentResult.NewVersionTag));

        //validate the payload is gone
        var getDeletedPayloadResponse = await ownerClient.V1.Drive.GetPayload(uploadedContentResult.File, payload.Key);
        Assert.That(getDeletedPayloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        //even tho the payload is gone, we should still be able to get the header and it should be updated
        var getHeaderResponse = await ownerClient.V1.Drive.GetFileHeader(uploadedContentResult.File);
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getHeaderResponse.Content!.FileState, Is.EqualTo(FileState.Active));
        Assert.That(getHeaderResponse.Content.FileMetadata.Payloads.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task CanGetPayloadInChunks()
    {
        var ownerClient = await LoginAsOwner();

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await ownerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false);

        // Frodo uploads content to channel drive
        const string uploadedContent = "I'm Mr. Underhill";

        // var uploadedPayload = "‘Rope!’ muttered Sam. ‘I knew I’d want it, if I hadn’t got it!’";
        var payload = new TestPayload()
        {
            Key = "rrrccca3r",
            Data = "What is happening with the encoding!?"
        };

        var uploadedContentResult = await UploadStandardFileToChannel(ownerClient, frodoChannelDrive, uploadedContent, payload);

        //Test whole payload is there
        var getPayloadResponse = await ownerClient.V1.Drive.GetPayload(uploadedContentResult.File, payload.Key);
        string payloadContent = await getPayloadResponse.Content!.ReadAsStringAsync();
        Assert.That(payloadContent, Is.EqualTo(payload.Data));

        // const string expectedChunk = "I knew I’d want it";
        // const string expectedChunk = "is happening";
        const string expectedChunk = "encoding!?";

        //get a chunk of the payload
        var chunk1 = new FileChunk()
        {
            Start = payload.Data.IndexOf(expectedChunk, StringComparison.Ordinal),
            Length = expectedChunk.Length
        };

        var getPayloadResponseChunk1 = await ownerClient.V1.Drive.GetPayload(uploadedContentResult.File, payload.Key, chunk1);
        string payloadContentChunk1 = await getPayloadResponseChunk1.Content!.ReadAsStringAsync();
        Assert.That(payloadContentChunk1, Is.EqualTo(expectedChunk));
    }

    [Test]
    [Ignore("for testing encoding")]
    public async Task CanGetPayloadInChunks_Weird()
    {
        var ownerClient = await LoginAsOwner();

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await ownerClient.Admin.CreateDrive(frodoChannelDrive, "A Channel Drive", allowAnonymousReads: false);

        // Frodo uploads content to channel drive
        const string uploadedContent = "I'm Mr. Underhill";

        var payload = new TestPayload()
        {
            Key = "sppee322p",
            Data = "‘Rope!’ muttered Sam. ‘I knew I’d want it, if I hadn’t got it!’"
        };

        var uploadedContentResult = await UploadStandardFileToChannel(ownerClient, frodoChannelDrive, uploadedContent, payload);

        //Test whole payload is there
        var getPayloadResponse = await ownerClient.V1.Drive.GetPayload(uploadedContentResult.File, payload.Key);
        string payloadContent = await getPayloadResponse.Content!.ReadAsStringAsync();
        Assert.That(payloadContent, Is.EqualTo(payload.Data));

        const string expectedChunk = "I knew I’d want it";
        // const string expectedChunk = "is happening";
        // const string expectedChunk = "encoding!?";

        //get a chunk of the payload
        var chunk1 = new FileChunk()
        {
            Start = 23,
            Length = expectedChunk.Length
        };

        var getPayloadResponseChunk1 = await ownerClient.V1.Drive.GetPayload(uploadedContentResult.File, payload.Key, chunk1);
        string payloadContentChunk1 = await getPayloadResponseChunk1.Content!.ReadAsStringAsync();
        Assert.That(payloadContentChunk1, Is.EqualTo(expectedChunk));
    }

    private static async Task<UploadResult> UploadStandardFileToChannel(OwnerSession client, TargetDrive targetDrive,
        string uploadedContent, TestPayload payload)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = 200,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        var manifest = new UploadManifest()
        {
            PayloadDescriptors = new List<UploadManifestPayloadDescriptor>()
            {
                new()
                {
                    Iv = null,
                    PayloadKey = payload.Key,
                    Thumbnails = new List<UploadedManifestThumbnailDescriptor>()
                }
            }
        };

        var payloads = new List<TestPayloadDefinition>()
        {
            new()
            {
                Iv = null,
                Key = payload.Key,
                ContentType = "application/x-binary",
                Content = payload.Data.ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
            }
        };

        var response = await client.V1.Drive.UploadNewFile(targetDrive, fileMetadata, manifest, payloads);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");
        return response.Content!;
    }
}
