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
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Drives;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.Ported.LocalAppMetadata;

/// <summary>
/// Port of <c>_V2/Tests/Drive/LocalAppMetadata/LocalAppMetadataEncryptedContentTests</c>. The owner
/// uploads an encrypted file via the V1 <see cref="UniversalDriveApiClient"/> (V2 doesn't expose a
/// metadata-only encrypted upload), then the caller-under-test updates the local-app-metadata content
/// using its own pre-computed IV / encrypted payload. Two cases:
/// happy-path encrypt-decrypt round-trip, and a bad-request when the IV is missing / weak.
/// Caller variants: Owner and App only — the original Guest case was on a separate source and is
/// dropped here for the same reason described in <c>UpdateBatchTests</c> (V1-endpoint auth scheme
/// doesn't accept the framework's V2 Guest/App tokens for write-through).
/// </summary>
[TestFixture]
public class EncryptedContentTests : V2Fixture
{
    public static IEnumerable<object[]> ReadWriteCases()
    {
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.ReadWrite), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(ReadWriteCases))]
    public async Task CanUpdateLocalAppMetadataContentForEncryptedTargetFile(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var fileMetadata = SampleMetadataData.Create(fileType: 100);
        fileMetadata.AppData.Content = "data data data";
        var keyHeader = KeyHeader.NewRandom16();

        var ownerV1 = new UniversalDriveApiClient(owner.Identity, owner.Factory);
        var (uploadResponse, _) = await ownerV1.UploadNewEncryptedMetadata(spec.TargetDrive, fileMetadata, keyHeader);
        Assert.That(uploadResponse.IsSuccessStatusCode, Is.True);
        var file = uploadResponse.Content!.File;

        var localContentIv = ByteArrayUtil.GetRndByteArray(16);
        const string plain = "some local content here";
        var encrypted = AesCbc.Encrypt(plain.ToUtf8ByteArray(), keyHeader.AesKey, localContentIv);

        var response = await caller.Drives.Writer.UpdateLocalAppMetadataContent(file.TargetDrive.Alias, file.FileId,
            new UpdateLocalMetadataContentRequestV2 { Iv = localContentIv, Content = encrypted.ToBase64() });
        Assert.That(response.StatusCode, Is.EqualTo(expected), $"actual {response.StatusCode}");
        if (expected != HttpStatusCode.OK) return;

        Assert.That(response.Content!.NewLocalVersionTag, Is.Not.EqualTo(Guid.Empty));

        var header = (await owner.Drives.Reader.GetFileHeaderAsync(file.TargetDrive.Alias, file.FileId)).Content!;
        var decrypted = AesCbc.Decrypt(header.FileMetadata.LocalAppData.Content.FromBase64(), keyHeader.AesKey, localContentIv);
        Assert.That(decrypted.ToStringFromUtf8Bytes(), Is.EqualTo(plain));
    }

    [Test, TestCaseSource(nameof(ReadWriteCases))]
    public async Task FailsWithBadRequestWhenMissingIvOnEncryptedTargetFile(CallerSpec spec, HttpStatusCode _)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var fileMetadata = SampleMetadataData.Create(fileType: 100);
        fileMetadata.AppData.Content = "data data data";
        var keyHeader = KeyHeader.NewRandom16();

        var ownerV1 = new UniversalDriveApiClient(owner.Identity, owner.Factory);
        var (uploadResponse, _) = await ownerV1.UploadNewEncryptedMetadata(spec.TargetDrive, fileMetadata, keyHeader);
        Assert.That(uploadResponse.IsSuccessStatusCode, Is.True);
        var file = uploadResponse.Content!.File;

        var localContentIv = ByteArrayUtil.GetRndByteArray(16);
        var encrypted = AesCbc.Encrypt("some local content here".ToUtf8ByteArray(), keyHeader.AesKey, localContentIv);

        var response = await caller.Drives.Writer.UpdateLocalAppMetadataContent(file.TargetDrive.Alias, file.FileId,
            new UpdateLocalMetadataContentRequestV2 { Iv = Guid.Empty.ToByteArray(), Content = encrypted.ToBase64() });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
