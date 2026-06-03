using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.DriveRead;

/// <summary>
/// Port of the three by-<c>UniqueId</c> reader tests from <c>_V2/Tests/Drive/DriveReaderTests/GetFileTests</c>:
/// <c>...OnAnonymousDriveV2ByUniqueId</c>, <c>...OnUnEncryptedFileOnSecuredDriveV2ByUniqueId</c>,
/// and the encrypted-file variant. Each owner-uploads a file (unencrypted or encrypted), then the
/// caller reads header / payload / thumbnail by client-supplied <c>UniqueId</c>. The encrypted case
/// additionally verifies the end-to-end client-side decryption path (content, payload, thumbnail).
/// </summary>
[TestFixture]
public class GetFileByUniqueIdTests : V2Fixture
{
    public static IEnumerable<object[]> AnonDriveCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    public static IEnumerable<object[]> SecuredDriveCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Secured(), DrivePermission.Read), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Secured(), DrivePermission.Read), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Secured(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Secured(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Owner(DriveSpec.Secured()), HttpStatusCode.OK];
    }

    public static IEnumerable<object[]> SecuredDriveEncryptedCases()
    {
        // No Guest-Read case: the encrypted file uses ACL=Connected, and unconnected guests can't read
        // it regardless of drive ACL. The original test simply omits that case.
        yield return [CallerSpec.App(DriveSpec.Secured(), DrivePermission.Read), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Secured(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Secured(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Owner(DriveSpec.Secured()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(AnonDriveCases))]
    public async Task CanGetHeaderAndPayloadAndThumbnailsOnAnonymousDriveByUniqueId(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var uniqueId = Guid.NewGuid();
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Anonymous;
        metadata.AppData.UniqueId = uniqueId;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        await OwnerUploadsFile(owner, spec.TargetDrive, metadata, payload);

        var driveId = spec.TargetDrive.Alias;

        var headerResponse = await caller.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, driveId);
        Assert.That(headerResponse.IsSuccessStatusCode, Is.True, $"header status was {headerResponse.StatusCode}");

        var header = headerResponse.Content!;
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(1));

        var payloadFromHeader = header.FileMetadata.GetPayloadDescriptor(payload.Key);
        Assert.That(payloadFromHeader, Is.Not.Null);
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(payloadFromHeader!.Iv, payload.Iv), Is.True);

        var payloadResponse = await caller.Drives.Reader.GetPayloadByUniqueIdAsync(uniqueId, driveId, payload.Key);
        Assert.That(payloadResponse.StatusCode, Is.EqualTo(expected));

        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        AssertPlaintextPayloadHeaders(payloadResponse.Headers!, payload);
        Assert.That(
            DriveFileUtility.TryParseLastModifiedHeader(payloadResponse.ContentHeaders!, out var lastModified),
            Is.True);
        Assert.That(lastModified.GetValueOrDefault().seconds, Is.EqualTo(payloadFromHeader.LastModified.seconds));
    }

    [Test, TestCaseSource(nameof(SecuredDriveCases))]
    public async Task CanGetHeaderAndPayloadAndThumbnailsOnUnEncryptedFileOnSecuredDriveByUniqueId(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var uniqueId = Guid.NewGuid();
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Authenticated;
        metadata.AppData.UniqueId = uniqueId;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        await OwnerUploadsFile(owner, spec.TargetDrive, metadata, payload);

        var driveId = spec.TargetDrive.Alias;

        var headerResponse = await caller.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, driveId);
        Assert.That(headerResponse.StatusCode, Is.EqualTo(expected));

        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        var header = headerResponse.Content!;
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(1));

        var payloadFromHeader = header.FileMetadata.GetPayloadDescriptor(payload.Key);
        Assert.That(payloadFromHeader, Is.Not.Null);
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(payloadFromHeader!.Iv, payload.Iv), Is.True);

        var payloadResponse = await caller.Drives.Reader.GetPayloadByUniqueIdAsync(uniqueId, driveId, payload.Key);
        Assert.That(payloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        AssertPlaintextPayloadHeaders(payloadResponse.Headers!, payload);
        Assert.That(DriveFileUtility.TryParseLastModifiedHeader(payloadResponse.ContentHeaders!, out _), Is.True);
    }

    [Test, TestCaseSource(nameof(SecuredDriveEncryptedCases))]
    public async Task CanGetHeaderAndPayloadAndThumbnailsOnEncryptedFileOnSecuredDriveByUniqueId(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var uniqueId = Guid.NewGuid();
        const string unencryptedContent = "some content here";
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AppData.Content = unencryptedContent;
        metadata.AccessControlList = AccessControlList.Connected;
        metadata.AppData.UniqueId = uniqueId;

        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        payload.Iv = ByteArrayUtil.GetRndByteArray(16);
        var thumbnail = payload.Thumbnails.Single();

        var keyHeader = KeyHeader.NewRandom16();
        var (uploadResult, encryptedJsonContent64, _, _) = await OwnerUploadsEncryptedFile(owner, spec.TargetDrive, metadata, payload, keyHeader);

        var driveId = spec.TargetDrive.Alias;

        var headerResponse = await caller.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, driveId);
        Assert.That(headerResponse.StatusCode, Is.EqualTo(expected));

        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        var header = headerResponse.Content!;
        Assert.That(header.FileId, Is.EqualTo(uploadResult.FileId));
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(1));
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo(encryptedJsonContent64));

        var decryptedContentBytes = keyHeader.Decrypt(header.FileMetadata.AppData.Content.FromBase64());
        Assert.That(decryptedContentBytes.ToStringFromUtf8Bytes(), Is.EqualTo(unencryptedContent));

        var payloadFromHeader = header.FileMetadata.GetPayloadDescriptor(payload.Key);
        Assert.That(payloadFromHeader, Is.Not.Null);
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(payloadFromHeader!.Iv, payload.Iv), Is.True);

        // Payload
        var payloadResponse = await caller.Drives.Reader.GetPayloadByUniqueIdAsync(uniqueId, driveId, payload.Key);
        Assert.That(payloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.That(payloadResponse.Headers!.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues), Is.True);
        Assert.That(bool.Parse(isEncryptedValues!.Single()), Is.True);

        Assert.That(payloadResponse.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues), Is.True);
        Assert.That(payloadKeyValues!.Single(), Is.EqualTo(payload.Key));

        Assert.That(payloadResponse.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, out var payloadSharedSecretValues), Is.True);
        var payloadEkh = EncryptedKeyHeader.FromBase64(payloadSharedSecretValues!.Single());
        Assert.That(payloadEkh, Is.Not.Null);
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(payloadEkh.Iv, payload.Iv), Is.True);
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(payloadEkh.EncryptedAesKey, header.SharedSecretEncryptedKeyHeader.EncryptedAesKey), Is.True);

        var sharedSecret = caller.Drives.Reader.GetSharedSecret();
        var decryptedPayloadKeyHeader = payloadEkh.DecryptAesToKeyHeader(ref sharedSecret);
        var decryptedPayloadBytes = decryptedPayloadKeyHeader.Decrypt(await payloadResponse.Content!.ReadAsByteArrayAsync());
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(decryptedPayloadBytes, payload.Content), Is.True);

        // Thumbnail
        var thumbResponse = await caller.Drives.Reader.GetThumbnailUniqueIdAsync(
            uniqueId, driveId, thumbnail.PixelWidth, thumbnail.PixelHeight, payload.Key, directMatchOnly: true);
        Assert.That(thumbResponse.IsSuccessStatusCode, Is.True);
        Assert.That(thumbResponse.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, out var thumbSharedSecretValues), Is.True);
        var thumbnailEkh = EncryptedKeyHeader.FromBase64(thumbSharedSecretValues!.Single());
        Assert.That(thumbnailEkh, Is.Not.Null);

        var decryptedThumbnailKeyHeader = thumbnailEkh.DecryptAesToKeyHeader(ref sharedSecret);
        var thumbBytes = await thumbResponse.Content!.ReadAsByteArrayAsync();
        var decryptedThumbnailBytes = decryptedThumbnailKeyHeader.Decrypt(thumbBytes);
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(decryptedThumbnailBytes, thumbnail.Content), Is.True);
    }

    private static async Task<CreateFileResult> OwnerUploadsFile(
        OwnerSession owner, TargetDrive targetDrive, UploadFileMetadata metadata, TestPayloadDefinition payload)
    {
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };
        var response = await owner.Drives.Writer.CreateNewUnencryptedFile(targetDrive.Alias, metadata, manifest, payloads);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"owner upload failed: {response.StatusCode}");
        return response.Content!;
    }

    private static async Task<(CreateFileResult upload, string encryptedJsonContent64,
            List<EncryptedAttachmentUploadResult> thumbs, List<EncryptedAttachmentUploadResult> payloads)>
        OwnerUploadsEncryptedFile(OwnerSession owner, TargetDrive targetDrive, UploadFileMetadata metadata,
            TestPayloadDefinition payload, KeyHeader keyHeader)
    {
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };
        var (response, encryptedJsonContent64, uploadedThumbnails, uploadedPayloads) =
            await owner.Drives.Writer.CreateEncryptedFile(
                targetDrive.Alias, metadata, new TransitOptions(), manifest, payloads,
                notificationOptions: null, keyHeader);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"owner encrypted upload failed: {response.StatusCode}");
        return (response.Content!, encryptedJsonContent64, uploadedThumbnails, uploadedPayloads);
    }

    private static void AssertPlaintextPayloadHeaders(System.Net.Http.Headers.HttpResponseHeaders headers, TestPayloadDefinition payload)
    {
        Assert.That(headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues), Is.True);
        Assert.That(bool.Parse(isEncryptedValues!.Single()), Is.False);

        Assert.That(headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues), Is.True);
        Assert.That(payloadKeyValues!.Single(), Is.EqualTo(payload.Key));

        Assert.That(headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues), Is.True);
        Assert.That(contentTypeValues!.Single(), Is.EqualTo(payload.ContentType));
    }
}
