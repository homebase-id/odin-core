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
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.Cdn;

/// <summary>
/// Reading a payload through the CDN must mean the same thing as reading it directly: the CDN fetches
/// via the same <c>/api/v2/drives/.../payload/...</c> endpoint, so it starts an expire-after-first-read
/// clock and is refused an expired file, exactly as a direct caller is.
///
/// CDN auth deliberately bypasses the per-file ACL. It must not bypass expiry - the expiry check sits
/// ahead of the ACL assert in <c>GetServerFileHeaderInternal</c> precisely so that it cannot.
/// </summary>
[TestFixture]
public class CdnFileTtlTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Sam];

    [Test]
    public async Task ACdnReadStartsTheExpireAfterFirstReadClockJustLikeADirectRead()
    {
        var owner = await LoginAsOwner(Identities.Sam);
        var cdn = CdnSession.Setup(Host, Identities.Sam);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "cdn ttl drive", allowAnonymousReads: false, allowCdn: true);

        var ttl = FileTtl.AfterFirstRead(TimeSpan.FromMinutes(20));
        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Authenticated);
        metadata.Ttl = ttl;

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var file = await UploadFile(owner, drive, metadata, payload);

        // Not read yet: still pending
        var before = await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId);
        Assert.That(before.Content!.FileMetadata.Ttl, Is.EqualTo(ttl), "unread file must still be pending");

        // The CDN is the first reader
        var resp = await cdn.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var after = await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId);
        Assert.That(FileTtl.IsAbsolute(after.Content!.FileMetadata.Ttl), Is.True,
            "a CDN read must start the clock, the same as a direct read");
    }

    [Test]
    public async Task AnExpiredFileIsRefusedToTheCdnEvenThoughCdnAuthBypassesTheAcl()
    {
        var owner = await LoginAsOwner(Identities.Sam);
        var cdn = CdnSession.Setup(Host, Identities.Sam);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "cdn ttl drive", allowAnonymousReads: false, allowCdn: true);

        var metadata = SampleMetadataData.Create(fileType: 101, acl: AccessControlList.Authenticated);
        metadata.Ttl = Odin.Core.Time.UnixTimeUtc.Now().milliseconds + 2_000;

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var file = await UploadFile(owner, drive, metadata, payload);

        // No "still alive" pre-assert: fixture setup alone can outlast a short Ttl, and
        // CdnPayloadResponseCacheControl already covers a live Ttl'd file reading OK through the CDN.
        await Task.Delay(3_000);

        var resp = await cdn.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "expiry must be enforced for the CDN too - the ACL bypass is not an expiry bypass");
    }

    /// <summary>
    /// Records what the origin currently tells the CDN edge about caching. See the assertion message:
    /// the Cache-Control clamp only fires for YouAuth/App callers, so a CDN response carries none.
    /// </summary>
    [Test]
    public async Task CdnPayloadResponseCacheControl()
    {
        var owner = await LoginAsOwner(Identities.Sam);
        var cdn = CdnSession.Setup(Host, Identities.Sam);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "cdn ttl drive", allowAnonymousReads: false, allowCdn: true);

        var metadata = SampleMetadataData.Create(fileType: 102, acl: AccessControlList.Authenticated);
        metadata.Ttl = FileTtl.After(TimeSpan.FromHours(1));

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var file = await UploadFile(owner, drive, metadata, payload);

        var resp = await cdn.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var hasCacheControl = resp.Headers!.TryGetValues("Cache-Control", out var values);
        TestContext.Out.WriteLine($"CDN Cache-Control present={hasCacheControl} value={(hasCacheControl ? string.Join(",", values!) : "<none>")}");

        Assert.That(hasCacheControl, Is.True,
            "the origin must tell the CDN edge how long it may keep an expiring payload");

        var maxAge = long.Parse(values!.Single().Split('=').Last());
        Assert.That(maxAge, Is.LessThanOrEqualTo((long)TimeSpan.FromHours(1).TotalSeconds),
            "the edge must not be told it can cache the payload for longer than the file lives");
    }

    private static async Task<CreateFileResult> UploadFile(
        OwnerSession owner, TargetDrive drive, UploadFileMetadata metadata, TestPayloadDefinition payload)
    {
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };
        var response = await owner.Drives.Writer.CreateNewUnencryptedFile(drive.Alias, metadata, manifest, payloads);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");
        return response.Content!;
    }
}
