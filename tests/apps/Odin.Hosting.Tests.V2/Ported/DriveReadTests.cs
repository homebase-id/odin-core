using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported;

/// <summary>
/// Port of the by-FileId reader tests from <c>_V2/Tests/Drive/DriveReaderTests/GetFileTests</c>:
/// <c>CanGetHeaderAndPayloadAndThumbnailsOnAnonymousDriveV2ByFileId</c> and
/// <c>...OnSecuredDriveV2ByFileId</c>. The owner uploads an unencrypted file with one payload
/// and one thumbnail; each caller variant then reads header / payload / thumbnail back. Asserts
/// status codes per the access-control matrix and validates payload metadata + headers on success.
/// </summary>
[TestFixture]
public class DriveReadTests : V2Fixture
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

    [Test, TestCaseSource(nameof(AnonDriveCases))]
    public async Task CanGetHeaderAndPayloadAndThumbnailsOnAnonymousDriveByFileId(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Anonymous;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var file = await OwnerUploadsFile(owner, spec.TargetDrive, metadata, payload);

        var headerResponse = await caller.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId);
        Assert.That(headerResponse.IsSuccessStatusCode, Is.True, $"header status was {headerResponse.StatusCode}");

        var header = headerResponse.Content!;
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(1));

        var payloadFromHeader = header.FileMetadata.GetPayloadDescriptor(payload.Key);
        Assert.That(payloadFromHeader, Is.Not.Null, "payload not found in header");
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(payloadFromHeader!.Iv, payload.Iv), Is.True);

        var payloadResponse = await caller.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);
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

        var thumbnail = payload.Thumbnails.Single();
        var thumbResponse = await caller.Drives.Reader.GetThumbnailAsync(
            file.DriveId, file.FileId, payload.Key,
            thumbnail.PixelWidth, thumbnail.PixelHeight, directMatchOnly: true);
        Assert.That(thumbResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var thumbBytes = await thumbResponse.Content!.ReadAsByteArrayAsync();
        Assert.That(thumbBytes.ToBase64(), Is.EqualTo(thumbnail.Content.ToBase64()), "thumbnail content mismatch");

        AssertPlaintextThumbnailHeaders(thumbResponse.Headers!, thumbnail.ContentType);
        Assert.That(DriveFileUtility.TryParseLastModifiedHeader(thumbResponse.ContentHeaders!, out _), Is.True);

        var thumbResponseNoSize = await caller.Drives.Reader.GetThumbnailAsync(file.DriveId, file.FileId, payload.Key);
        Assert.That(thumbResponseNoSize.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test, TestCaseSource(nameof(SecuredDriveCases))]
    public async Task CanGetHeaderAndPayloadAndThumbnailsOnSecuredDriveByFileId(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Authenticated;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var file = await OwnerUploadsFile(owner, spec.TargetDrive, metadata, payload);

        var headerResponse = await caller.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId);
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

        var payloadResponse = await caller.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);
        Assert.That(payloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        AssertPlaintextPayloadHeaders(payloadResponse.Headers!, payload);
        Assert.That(
            DriveFileUtility.TryParseLastModifiedHeader(payloadResponse.ContentHeaders!, out var lastModified),
            Is.True);
        Assert.That(lastModified.GetValueOrDefault().seconds, Is.EqualTo(payloadFromHeader.LastModified.seconds));
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

    private static void AssertPlaintextPayloadHeaders(System.Net.Http.Headers.HttpResponseHeaders headers, TestPayloadDefinition payload)
    {
        Assert.That(headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues), Is.True);
        Assert.That(bool.Parse(isEncryptedValues!.Single()), Is.False);

        Assert.That(headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues), Is.True);
        Assert.That(payloadKeyValues!.Single(), Is.EqualTo(payload.Key));

        Assert.That(headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues), Is.True);
        Assert.That(contentTypeValues!.Single(), Is.EqualTo(payload.ContentType));

        headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, out var ssEncValues);
        Assert.That(ssEncValues == null || ssEncValues.All(string.IsNullOrEmpty), Is.True,
            "SharedSecretEncryptedHeader64 should be absent or empty on plaintext payload");
    }

    private static void AssertPlaintextThumbnailHeaders(System.Net.Http.Headers.HttpResponseHeaders headers, string contentType)
    {
        Assert.That(headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues), Is.True);
        Assert.That(bool.Parse(isEncryptedValues!.Single()), Is.False);

        Assert.That(headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues), Is.True);
        Assert.That(contentTypeValues!.Single(), Is.EqualTo(contentType));

        headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, out var ssEncValues);
        Assert.That(ssEncValues == null || ssEncValues.All(string.IsNullOrEmpty), Is.True,
            "SharedSecretEncryptedHeader64 should be absent or empty on plaintext thumbnail");
    }
}
