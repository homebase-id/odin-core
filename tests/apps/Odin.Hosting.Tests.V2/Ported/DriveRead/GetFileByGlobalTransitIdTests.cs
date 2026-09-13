using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveRead;

/// <summary>
/// Reads of payloads and thumbnails on a local drive addressed by globalTransitId. A follower's feed
/// row carries only the author's globalTransitId, so this is the only way to address a followed
/// post's media on the author's host without first resolving it to a fileId.
/// </summary>
[TestFixture]
public class GetFileByGlobalTransitIdTests : V2Fixture
{
    public static IEnumerable<object[]> AnonDriveCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    // Matches the by-uid matrix. The gtid lookup itself gates on read-or-write, but the payload read
    // re-asserts read permission (DriveStorageServiceBase.GetPayloadStreamAsync), so a write-only
    // caller still cannot pull the bytes.
    public static IEnumerable<object[]> SecuredDriveCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Secured(), DrivePermission.Read), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Secured(), DrivePermission.Read), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Secured(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Secured(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Owner(DriveSpec.Secured()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(AnonDriveCases))]
    public async Task CanGetPayloadAndThumbnailOnAnonymousDriveByGtid(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Anonymous;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var upload = await OwnerUploadsFile(owner, spec.TargetDrive, metadata, payload);

        var gtid = upload.GlobalTransitId;
        Assert.That(gtid, Is.Not.Null, "a standard-file upload always mints a globalTransitId");

        var driveId = spec.TargetDrive.Alias;

        var payloadResponse = await caller.Drives.Reader.GetPayloadByGtidAsync(gtid!.Value, driveId, payload.Key);
        Assert.That(payloadResponse.StatusCode, Is.EqualTo(expected));

        AssertPlaintextPayloadHeaders(payloadResponse.Headers!, payload);

        var thumb = payload.Thumbnails.First();
        var thumbResponse = await caller.Drives.Reader.GetThumbnailByGtidAsync(
            gtid.Value, driveId, thumb.PixelWidth, thumb.PixelHeight, payload.Key);
        Assert.That(thumbResponse.StatusCode, Is.EqualTo(expected));
    }

    [Test, TestCaseSource(nameof(SecuredDriveCases))]
    public async Task CanGetPayloadOnSecuredDriveByGtid(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Authenticated;
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var upload = await OwnerUploadsFile(owner, spec.TargetDrive, metadata, payload);

        var gtid = upload.GlobalTransitId;
        Assert.That(gtid, Is.Not.Null);

        var payloadResponse = await caller.Drives.Reader.GetPayloadByGtidAsync(
            gtid!.Value, spec.TargetDrive.Alias, payload.Key);
        Assert.That(payloadResponse.StatusCode, Is.EqualTo(expected));
    }

    [Test]
    public async Task UnknownGtidReturns404()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, _) = await SetupCallerWithOwner(spec);

        var response = await caller.Drives.Reader.GetPayloadByGtidAsync(
            Guid.NewGuid(), spec.TargetDrive.Alias, "test_key_1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
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

    private static void AssertPlaintextPayloadHeaders(
        System.Net.Http.Headers.HttpResponseHeaders headers, TestPayloadDefinition payload)
    {
        if (!headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncryptedValues))
        {
            return;
        }

        Assert.That(bool.Parse(isEncryptedValues.Single()), Is.False);
        Assert.That(headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKeyValues), Is.True);
        Assert.That(payloadKeyValues!.Single(), Is.EqualTo(payload.Key));
        Assert.That(headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentTypeValues), Is.True);
        Assert.That(contentTypeValues!.Single(), Is.EqualTo(payload.ContentType));
    }
}
