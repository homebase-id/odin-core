using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_V2/Tests/Drive/WriteFileTests/DirectDrivePayloadTestsV2.CanDeletePayloadOnExistingFileAndMetadataIsAutomaticallyUpdated</c>.
/// Owner uploads a file with one payload, then the caller-under-test deletes the payload via the V2
/// writer endpoint. On success the file header's version tag changes, the payload list empties, and
/// fetching the payload bytes returns 404.
/// </summary>
[TestFixture]
public class PayloadTests : V2Fixture
{
    public static IEnumerable<object[]> CallerCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(CallerCases))]
    public async Task CanDeletePayloadOnExistingFileAndMetadataIsAutomaticallyUpdated(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        var upload = await owner.Drives.Writer.CreateNewUnencryptedFile(spec.TargetDrive.Alias, metadata, manifest, payloads);
        Assert.That(upload.IsSuccessStatusCode, Is.True);
        var uploadResult = upload.Content!;

        var headerBefore = await owner.Drives.Reader.GetFileHeaderAsync(uploadResult.DriveId, uploadResult.FileId);
        Assert.That(headerBefore.IsSuccessStatusCode, Is.True);
        Assert.That(headerBefore.Content!.FileMetadata.Payloads.Count(), Is.EqualTo(1));
        var descriptor = headerBefore.Content.FileMetadata.Payloads.Single(p => p.KeyEquals(payload.Key));
        Assert.That(descriptor.ContentType, Is.EqualTo(payload.ContentType));
        Assert.That(descriptor.BytesWritten, Is.EqualTo(payload.Content.Length));

        var deleteResponse = await caller.Drives.Writer.DeletePayload(
            uploadResult.DriveId, uploadResult.FileId, payload.Key, headerBefore.Content.FileMetadata.VersionTag);
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(expected), $"actual {deleteResponse.StatusCode}");
        if (expected != HttpStatusCode.OK) return;

        var deleteResult = deleteResponse.Content!;
        Assert.That(deleteResult.NewVersionTag, Is.Not.EqualTo(headerBefore.Content.FileMetadata.VersionTag));
        Assert.That(deleteResult.NewVersionTag, Is.Not.EqualTo(Guid.Empty));

        var headerAfter = await owner.Drives.Reader.GetFileHeaderAsync(uploadResult.DriveId, uploadResult.FileId);
        Assert.That(headerAfter.IsSuccessStatusCode, Is.True);
        Assert.That(headerAfter.Content!.FileMetadata.VersionTag, Is.EqualTo(deleteResult.NewVersionTag));
        Assert.That(headerAfter.Content.FileMetadata.Payloads.Any(), Is.False);

        var getPayload = await owner.Drives.Reader.GetPayloadAsync(uploadResult.DriveId, uploadResult.FileId, payload.Key);
        Assert.That(getPayload.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
