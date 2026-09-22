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
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/DirectDriveGeneralFileTests_Encrypted</c>. Covers updating an
/// encrypted file's metadata with a metadata-only storage intent — once for a header-only file and
/// once for a file that already carries an encrypted payload + thumbnail — across the caller matrix,
/// verifying the re-encrypted content only decrypts with the new IV and that the payload and
/// thumbnail still decrypt with the original AES key.
/// </summary>
/// <remarks>
/// These drive the <b>V1</b> drive endpoints (<c>/api/owner/v1/drive/files/...</c>) through the
/// in-process host via the V1-shaped <see cref="UniversalDriveApiClient"/>, reached here through
/// <c>caller.V1.Drive</c> / <c>owner.V1.Drive</c>.
/// </remarks>
[TestFixture]
public class V1GeneralFileEncryptedTests : V2Fixture
{
    /// <summary>
    /// Updating metadata is a write, so a read-only guest and a write-only app (which cannot read
    /// back the version tag it needs) are both refused; owner and a read-write app succeed.
    /// </summary>
    public static IEnumerable<object[]> UpdateMetadataCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.ReadWrite), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(UpdateMetadataCases))]
    public async Task CanUpdateEncryptedMetadataWithStorageIntent_MetadataOnly(CallerSpec spec, HttpStatusCode expected)
    {
        // Setup
        var (caller, _) = await SetupCallerWithOwner(spec);

        const string originalContent = "some content here";
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AppData.Content = originalContent;

        // Act
        var callerDriveClient = caller.V1.Drive;
        var originalKeyHeader = KeyHeader.NewRandom16();
        var (response, originalEncryptedJsonContent64) =
            await callerDriveClient.UploadNewEncryptedMetadata(spec.TargetDrive, uploadedFileMetadata, originalKeyHeader);

        Assert.That(response.StatusCode, Is.EqualTo(expected));

        if (expected != HttpStatusCode.OK) return;

        var uploadResult = response.Content;
        var getHeaderResponse1 = await callerDriveClient.GetFileHeader(uploadResult!.File);
        Assert.That(getHeaderResponse1.IsSuccessStatusCode, Is.True);
        var uploadedFile1 = getHeaderResponse1.Content;

        const string updatedContent = "updated information and content here";
        var updatedMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        updatedMetadata.AppData.Content = updatedContent;
        updatedMetadata.VersionTag = uploadedFile1!.FileMetadata.VersionTag;
        updatedMetadata.IsEncrypted = true;

        var newKeyHeader = new KeyHeader()
        {
            Iv = ByteArrayUtil.GetRndByteArray(16),
            AesKey = new SensitiveByteArray(originalKeyHeader.AesKey.GetKey())
        };

        var (updateResponse, updatedEncryptedJsonContent64) =
            await callerDriveClient.UpdateExistingEncryptedMetadata(uploadResult.File, newKeyHeader, updatedMetadata);

        Assert.That(updateResponse.IsSuccessStatusCode, Is.True);
        Assert.That(updatedEncryptedJsonContent64, Is.Not.EqualTo(originalEncryptedJsonContent64),
            "original encrypted content should not match updated encrypted content since the IV changed");

        // grab the file again
        var getHeaderResponse2 = await callerDriveClient.GetFileHeader(uploadResult.File);
        Assert.That(getHeaderResponse2.IsSuccessStatusCode, Is.True);

        var theUpdatedFile = getHeaderResponse2.Content;
        var updatedContentDecryptedWithOriginalKeyHeader =
            originalKeyHeader.Decrypt(Convert.FromBase64String(theUpdatedFile!.FileMetadata.AppData.Content));
        Assert.That(updatedContentDecryptedWithOriginalKeyHeader.ToStringFromUtf8Bytes(), Is.Not.EqualTo(updatedContent));

        var updatedContentDecryptedWithNewKeyHeader = newKeyHeader.Decrypt(Convert.FromBase64String(theUpdatedFile.FileMetadata.AppData.Content));
        Assert.That(updatedContentDecryptedWithNewKeyHeader.ToStringFromUtf8Bytes(), Is.EqualTo(updatedContent));
    }

    [Test, TestCaseSource(nameof(UpdateMetadataCases))]
    public async Task CanUpdateEncryptedMetadata_That_Has_A_Payload_WithStorageIntent_MetadataOnly(CallerSpec spec,
        HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        // upload metadata
        const string originalContent = "original content is here";
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AppData.Content = originalContent;

        var p = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        p.Iv = ByteArrayUtil.GetRndByteArray(16);
        List<TestPayloadDefinition> testPayloads = [p];

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var originalKeyHeader = KeyHeader.NewRandom16();

        // upload a file with payloads
        var (response, _, _, _) =
            await ownerDriveClient.UploadNewEncryptedFile(spec.TargetDrive, originalKeyHeader, uploadedFileMetadata, uploadManifest,
                testPayloads);
        Assert.That(response.IsSuccessStatusCode, Is.True);
        var uploadResult = response.Content;
        Assert.That(uploadResult, Is.Not.Null);

        // Get the file from the server
        var getOriginalHeaderResponse = await ownerDriveClient.GetFileHeader(uploadResult!.File);
        Assert.That(getOriginalHeaderResponse.IsSuccessStatusCode, Is.True);
        var uploadedFile1 = getOriginalHeaderResponse.Content;

        //
        // Now, Change just header
        //
        var callerDriveClient = caller.V1.Drive;
        const string updatedContent = "updated information and content here";
        var updatedMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        updatedMetadata.AppData.Content = updatedContent;
        updatedMetadata.VersionTag = uploadedFile1!.FileMetadata.VersionTag;
        updatedMetadata.IsEncrypted = true;

        var newKeyHeader = new KeyHeader()
        {
            Iv = ByteArrayUtil.GetRndByteArray(16),
            AesKey = new SensitiveByteArray(originalKeyHeader.AesKey.GetKey())
        };

        var (updateResponse, _) =
            await callerDriveClient.UpdateExistingEncryptedMetadata(uploadResult.File, newKeyHeader, updatedMetadata);
        Assert.That(updateResponse.StatusCode, Is.EqualTo(expected));

        // Let's test more
        if (expected != HttpStatusCode.OK) return;

        // then validate that I can decrypt the payloads

        var getUpdatedHeaderResponse = await callerDriveClient.GetFileHeader(uploadResult.File);
        Assert.That(getUpdatedHeaderResponse.IsSuccessStatusCode, Is.True);
        var updatedHeaderResponse = getUpdatedHeaderResponse.Content;
        Assert.That(updatedHeaderResponse, Is.Not.Null);
        Assert.That(updatedHeaderResponse!.FileMetadata.AppData.Content, Is.Not.EqualTo(uploadedFileMetadata.AppData.Content));
        Assert.That(updatedHeaderResponse.FileMetadata.Payloads.Count(), Is.EqualTo(testPayloads.Count));

        // Get the payloads
        var definition = testPayloads.First();
        var getPayloadResponse = await ownerDriveClient.GetPayload(uploadResult.File, definition.Key);
        Assert.That(getPayloadResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getPayloadResponse.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues), Is.True);

        //
        // Validate that I can still decrypt using the original AES key
        //
        var payloadDescriptor = updatedHeaderResponse.FileMetadata.Payloads.Single(pd => pd.Key == payloadKeyValues!.First());
        var payloadKeyHeader = new KeyHeader()
        {
            Iv = payloadDescriptor.Iv,
            AesKey = new SensitiveByteArray(originalKeyHeader.AesKey.GetKey())
        };

        var encryptedPayloadContent = (await getPayloadResponse.Content!.ReadAsStreamAsync()).ToByteArray();
        var decryptedPayloadContent = payloadKeyHeader.Decrypt(encryptedPayloadContent);
        Assert.That(decryptedPayloadContent.ToStringFromUtf8Bytes(), Is.EqualTo(definition.Content.ToStringFromUtf8Bytes()));

        // Check all the thumbnails
        var thumbnail = definition.Thumbnails.First();

        var getThumbnailResponse = await ownerDriveClient.GetThumbnail(uploadResult.File,
            thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);
        Assert.That(getThumbnailResponse.IsSuccessStatusCode, Is.True);

        var encryptedThumbnailContent = (await getThumbnailResponse.Content!.ReadAsStreamAsync()).ToByteArray();
        var decryptedThumbnailContent = payloadKeyHeader.Decrypt(encryptedThumbnailContent);
        Assert.That(decryptedThumbnailContent.ToStringFromUtf8Bytes(), Is.EqualTo(thumbnail.Content.ToStringFromUtf8Bytes()));
    }
}
