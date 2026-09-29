using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.Ported.LocalAppMetadata;

/// <summary>
/// Port of <c>_Universal/DriveTests/LocalAppMetadata/LocalAppMetadataEncryptedContentTests</c>. The
/// owner uploads an encrypted, metadata-only file; the caller-under-test then writes local-app-metadata
/// content encrypted with the same key header and its own IV. Two cases: the happy-path encrypt /
/// decrypt round-trip, and a bad request when the IV is missing or weak.
/// </summary>
/// <remarks>
/// These drive the <b>V1</b> local-app-metadata endpoints (<c>/api/owner/v1/drive/files/...</c>)
/// through the in-process host via the V1-shaped <see cref="UniversalDriveApiClient"/>, resolved
/// against the fixture's <c>Factory</c>. That makes them distinct from the sibling
/// <see cref="EncryptedContentTests"/>, which covers the same behaviour on the V2 endpoints.
/// </remarks>
[TestFixture]
public class V1EncryptedContentTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Pippin];

    public static IEnumerable<object[]> OwnerAllowed()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    public static IEnumerable<object[]> AppAllowed()
    {
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task CanUpdateLocalAppMetadataContentForEncryptedTargetFile(CallerSpec spec,
        HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "data data data";

        var keyHeader = KeyHeader.NewRandom16();

        // Act
        var (prepareFileResponse, _) =
            await ownerDriveClient.UploadNewEncryptedMetadata(spec.TargetDrive, uploadedFileMetadata, keyHeader);
        Assert.That(prepareFileResponse.IsSuccessStatusCode, Is.True);
        var targetFile = prepareFileResponse.Content!.File;

        // Act - update the local app metadata
        var callerDriveClient = caller.V1.Drive;

        var localContentIv = ByteArrayUtil.GetRndByteArray(16);
        var content = "some local content here";
        var encryptedLocalMetadataContent = AesCbc.Encrypt(content.ToUtf8ByteArray(), keyHeader.AesKey, localContentIv);

        var request = new UpdateLocalMetadataContentRequest()
        {
            Iv = localContentIv,
            File = targetFile,
            Content = encryptedLocalMetadataContent.ToBase64()
        };

        var response = await callerDriveClient.UpdateLocalAppMetadataContent(request);
        var result = response.Content;
        Assert.That(result!.NewLocalVersionTag, Is.Not.EqualTo(Guid.Empty));

        // Assert - getting the file should include the metadata
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        // Get the file and see that it's updated
        var updatedFileResponse = await ownerDriveClient.GetFileHeader(targetFile);
        var theUpdatedFile = updatedFileResponse.Content;

        var decryptedBytes = AesCbc.Decrypt(theUpdatedFile!.FileMetadata.LocalAppData.Content.FromBase64(), keyHeader.AesKey,
            request.Iv);
        Assert.That(decryptedBytes.ToStringFromUtf8Bytes(), Is.EqualTo(content));
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task FailsWithBadRequestWhenMissingIvOnEncryptedTargetFile(CallerSpec spec,
        HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "data data data";

        var keyHeader = KeyHeader.NewRandom16();

        // Act
        var (prepareFileResponse, _) =
            await ownerDriveClient.UploadNewEncryptedMetadata(spec.TargetDrive, uploadedFileMetadata, keyHeader);
        Assert.That(prepareFileResponse.IsSuccessStatusCode, Is.True);
        var targetFile = prepareFileResponse.Content!.File;

        // Act - update the local app metadata
        var callerDriveClient = caller.V1.Drive;

        var localContentIv = ByteArrayUtil.GetRndByteArray(16);
        var content = "some local content here";
        var encryptedLocalMetadataContent = AesCbc.Encrypt(content.ToUtf8ByteArray(), keyHeader.AesKey, localContentIv);

        var request = new UpdateLocalMetadataContentRequest()
        {
            Iv = Guid.Empty.ToByteArray(), //weak or no key
            File = targetFile,
            Content = encryptedLocalMetadataContent.ToBase64()
        };

        var response = await callerDriveClient.UpdateLocalAppMetadataContent(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
