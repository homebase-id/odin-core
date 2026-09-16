using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/UpdateBatch/DirectDriveLocalUpdateBatchEncryptedTests</c>. Runs
/// the update-batch endpoint with <see cref="UpdateLocale.Local"/> against an encrypted file —
/// once header-only and once swapping one encrypted payload+thumbnail for another — across the
/// caller matrix, then verifies the stored header, the added/removed payloads and that the file is
/// still found by its new data type.
/// </summary>
/// <remarks>
/// These drive the <b>V1</b> drive endpoints (<c>/api/owner/v1/drive/files/...</c>) through the
/// in-process host via the V1-shaped <see cref="UniversalDriveApiClient"/>, reached here through
/// <c>caller.V1.Drive</c> / <c>owner.V1.Drive</c>.
/// </remarks>
[TestFixture]
public class V1LocalUpdateBatchEncryptedTests : V2Fixture
{
    /// <summary>
    /// Updating a batch is a write that also reads back state, so both guests are refused; owner and
    /// a read-write app succeed.
    /// </summary>
    public static IEnumerable<object[]> UpdateBatchCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.ReadWrite), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchEncryptedWithoutPayloads(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        //
        // Setup - upload a new file with payloads
        //
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "some new content here...";

        var keyHeader = KeyHeader.NewRandom16();
        var (uploadNewFileResponse, _) =
            await ownerDriveClient.UploadNewEncryptedMetadata(spec.TargetDrive, uploadedFileMetadata, keyHeader);
        Assert.That(uploadNewFileResponse.IsSuccessStatusCode, Is.True);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult!.File;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here...";
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,

            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToFileIdentifier(),
            Recipients = default,
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            }
        };

        keyHeader.Iv = ByteArrayUtil.GetRndByteArray(16);
        var callerDriveClient = caller.V1.Drive;
        var (updateFileResponse, updatedEncryptedMetadataContent64, _, _) = await callerDriveClient.UpdateEncryptedFile(
            updateInstructionSet,
            updatedFileMetadata,
            [], keyHeader);

        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK) return;

        //
        // Get the updated file and test it
        //
        var getHeaderResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.IsEncrypted, Is.True);
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo(updatedEncryptedMetadataContent64));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(header.FileMetadata.Payloads.Any(), Is.False);

        var searchResponse = await ownerDriveClient.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = targetFile.TargetDrive,
                DataType = [updatedFileMetadata.AppData.DataType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(searchResponse.IsSuccessStatusCode, Is.True);
        var theFileSearchResult = searchResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(theFileSearchResult, Is.Not.Null);
        Assert.That(theFileSearchResult!.FileId, Is.EqualTo(targetFile.FileId));
    }

    [Test, TestCaseSource(nameof(UpdateBatchCases))]
    public async Task CanUpdateBatchEncryptedWith1PayloadsAnd1Thumbnails(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        //
        // Setup - upload a new file with payloads
        //
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "some new content here...";
        var payloadThatWillBeDeleted = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        payloadThatWillBeDeleted.Iv = ByteArrayUtil.GetRndByteArray(16);
        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = [payloadThatWillBeDeleted.ToPayloadDescriptor()]
        };

        var keyHeader = KeyHeader.NewRandom16();
        var (uploadNewFileResponse, _, _, _) = await ownerDriveClient.UploadNewEncryptedFile(spec.TargetDrive, keyHeader,
            uploadedFileMetadata, uploadManifest, [payloadThatWillBeDeleted]);
        Assert.That(uploadNewFileResponse.IsSuccessStatusCode, Is.True);

        var uploadResult = uploadNewFileResponse.Content;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 777;
        updatedFileMetadata.VersionTag = uploadResult!.NewVersionTag;

        var targetFile = uploadResult.File;
        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition2();
        payloadToAdd.Iv = ByteArrayUtil.GetRndByteArray(16);

        // create instruction set
        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,

            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToFileIdentifier(),
            Recipients = default,
            Manifest = new UploadManifest
            {
                PayloadDescriptors =
                [
                    payloadToAdd.ToPayloadDescriptor(PayloadUpdateOperationType.AppendOrOverwrite),
                    new UploadManifestPayloadDescriptor()
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.DeletePayload,
                        PayloadKey = payloadThatWillBeDeleted.Key
                    }
                ]
            }
        };

        keyHeader.Iv = ByteArrayUtil.GetRndByteArray(16);
        var callerDriveClient = caller.V1.Drive;
        var (updateFileResponse, updatedEncryptedMetadataContent64, encryptedPayloads, encryptedThumbnails) =
            await callerDriveClient.UpdateEncryptedFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd], keyHeader);

        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK) return;

        //
        // Get the updated file and test it
        //
        var getHeaderResponse = await ownerDriveClient.GetFileHeader(targetFile);
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.IsEncrypted, Is.True);
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo(updatedEncryptedMetadataContent64));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(1));
        Assert.That(header.FileMetadata.Payloads.All(pd => pd.Key != payloadThatWillBeDeleted.Key), Is.True,
            "payload 1 should have been removed");
        Assert.That(header.FileMetadata.Payloads.Any(pd => pd.Key == payloadToAdd.Key), Is.True,
            "payloadToAdd should have been, well, added :)");

        //
        // Ensure payloadToAdd add is added
        //
        var getPayloadToAddResponse = await ownerDriveClient.GetPayload(targetFile, payloadToAdd.Key);
        Assert.That(getPayloadToAddResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getPayloadToAddResponse.ContentHeaders!.LastModified.HasValue, Is.True);
        Assert.That(getPayloadToAddResponse.ContentHeaders.LastModified.GetValueOrDefault(),
            Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

        var content = (await getPayloadToAddResponse.Content!.ReadAsStreamAsync()).ToByteArray();
        Assert.That(content.ToBase64(), Is.EqualTo(encryptedPayloads.Single(p => p.Key == payloadToAdd.Key).EncryptedContent64));

        // Check all the thumbnails
        foreach (var thumbnail in payloadToAdd.Thumbnails)
        {
            var getThumbnailResponse = await ownerDriveClient.GetThumbnail(targetFile, thumbnail.PixelWidth,
                thumbnail.PixelHeight, payloadToAdd.Key);

            Assert.That(getThumbnailResponse.IsSuccessStatusCode, Is.True);
            Assert.That(getThumbnailResponse.ContentHeaders!.LastModified.HasValue, Is.True);
            Assert.That(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault(),
                Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

            var thumbContent = (await getThumbnailResponse.Content!.ReadAsStreamAsync()).ToByteArray();
            Assert.That(thumbContent.ToBase64(),
                Is.EqualTo(encryptedThumbnails.Single(p => p.Key == payloadToAdd.Key).EncryptedContent64));
        }

        //
        // Ensure we get 404 for the payload1
        //
        var getPayload1Response = await ownerDriveClient.GetPayload(targetFile, payloadThatWillBeDeleted.Key);
        Assert.That(getPayload1Response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var searchResponse = await ownerDriveClient.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = targetFile.TargetDrive,
                DataType = [updatedFileMetadata.AppData.DataType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(searchResponse.IsSuccessStatusCode, Is.True);
        var theFileSearchResult = searchResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(theFileSearchResult, Is.Not.Null);
        Assert.That(theFileSearchResult!.FileId, Is.EqualTo(targetFile.FileId));
    }
}
