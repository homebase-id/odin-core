using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Cdn;

/// <summary>
/// Port of <c>_V2/Tests/CDN/CdnTests</c>. The CDN caller is a bearer-token-only client — no shared
/// secret, no owner session, no app/youauth flow. The host's <c>Cdn__RequiredAuthToken</c> is
/// pre-seeded in <see cref="Hosting.OdinHost"/>'s env baseline so the token the framework hands
/// out via <see cref="CdnSession"/> and the token the host trusts are the same value.
/// </summary>
[TestFixture]
public class CdnTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Sam];

    [Test]
    public async Task GenericCdnPingShouldSucceed()
    {
        var cdn = CdnSession.Setup(Host, Identities.Sam);

        var response = await cdn.Cdn.CdnPing(10);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content?.Headers.ContentType?.MediaType, Is.EqualTo("application/octet-stream"));

        var body = await response.Content!.ReadAsByteArrayAsync();
        Assert.That(body, Is.EqualTo(new byte[10].Select(_ => (byte)'X').ToArray()));

        var cdnHeader = response.Headers.GetValues(OdinHeaderNames.OdinCdnPayload).FirstOrDefault();
        Assert.That(cdnHeader, Is.EqualTo("https://cdn.ravenhosting.cloud"));
    }

    [Test]
    public async Task GenericCdnPingOnBadPathShouldFail()
    {
        var cdn = CdnSession.Setup(Host, Identities.Sam);

        var response = await cdn.Cdn.CdnPingBadPath();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var cdnHeader = response.Headers.GetValues(OdinHeaderNames.OdinCdnPayload).FirstOrDefault();
        Assert.That(cdnHeader, Is.EqualTo("https://cdn.ravenhosting.cloud"));
    }

    [Test]
    public async Task GenericCdnPingWithoutCdnTokenShouldFail()
    {
        using var client = Host.CreateClient();
        var url = $"https://{Identities.Sam}/api/v2/drives/cdn-ping/payload/10";

        // No Authorization header at all.
        var response = await client.GetAsync(url);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Is.EqualTo("Missing or invalid Authorization header"));

        // With an invalid bearer.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        response = await client.GetAsync(url);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Is.EqualTo("Incorrect bearer token"));
    }

    [Test]
    public async Task FailToGetHeaderAsCdn()
    {
        var cdn = CdnSession.Setup(Host, Identities.Sam);

        // The CDN bearer must not satisfy the file-header endpoint (CDN is payload-only). The
        // file-id is irrelevant — the auth gate trips before any drive lookup.
        var response = await cdn.Drives.Reader.GetFileHeaderAsync(Guid.Empty, Guid.Empty);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            $"actual {response.StatusCode}");

        var cdnHeader = response.Headers.GetValues(OdinHeaderNames.OdinCdnPayload).FirstOrDefault();
        Assert.That(cdnHeader, Is.EqualTo("https://cdn.ravenhosting.cloud"));
    }

    [Test]
    public async Task CanGetPayloadAndThumbnailsOnAnonymousDriveV2()
    {
        var owner = await LoginAsOwner(Identities.Sam);
        var cdn = CdnSession.Setup(Host, Identities.Sam);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "anon drive", allowAnonymousReads: true);

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var file = await UploadFile(owner, drive, metadata, payload);

        var resp = await cdn.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {resp.StatusCode}");

        AssertPlaintextPayloadHeaders(resp.Headers!, payload);
        Assert.That(DriveFileUtility.TryParseLastModifiedHeader(resp.ContentHeaders!, out _), Is.True);

        var headerResp = await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId);
        Assert.That(headerResp.IsSuccessStatusCode, Is.True);
        var header = headerResp.Content!;
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(1));
        var payloadFromHeader = header.FileMetadata.GetPayloadDescriptor(payload.Key);
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(payloadFromHeader!.Iv, payload.Iv), Is.True);
    }

    [Test]
    public async Task CanGetPayloadAndThumbnailsOnSecuredDriveV2()
    {
        var owner = await LoginAsOwner(Identities.Sam);
        var cdn = CdnSession.Setup(Host, Identities.Sam);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "secured drive", allowAnonymousReads: false, allowCdn: true);

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Authenticated);
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var file = await UploadFile(owner, drive, metadata, payload);

        // CDN auth bypasses drive ACL for payload reads — even an authenticated-only drive is
        // reachable via the CDN bearer.
        var resp = await cdn.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {resp.StatusCode}");

        AssertPlaintextPayloadHeaders(resp.Headers!, payload);

        var headerResp = await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId);
        Assert.That(headerResp.IsSuccessStatusCode, Is.True);
        var payloadFromHeader = headerResp.Content!.FileMetadata.GetPayloadDescriptor(payload.Key);
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(payloadFromHeader!.Iv, payload.Iv), Is.True);

        Assert.That(DriveFileUtility.TryParseLastModifiedHeader(resp.ContentHeaders!, out var lastMod), Is.True);
        Assert.That(lastMod.GetValueOrDefault().seconds, Is.EqualTo(payloadFromHeader.LastModified.seconds));
    }

    [Test]
    public async Task CanGetEncryptedPayloadAndThumbnailsOnSecuredDriveV2()
    {
        var owner = await LoginAsOwner(Identities.Sam);
        var cdn = CdnSession.Setup(Host, Identities.Sam);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "secured encrypted drive", allowAnonymousReads: false, allowCdn: true);

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        metadata.AppData.Content = "original content is here";
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        payload.Iv = ByteArrayUtil.GetRndByteArray(16);

        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };
        var keyHeader = KeyHeader.NewRandom16();

        var (uploadResp, _, uploadedThumbs, uploadedPayloads) = await owner.Drives.Writer.CreateEncryptedFile(
            drive.Alias, metadata, new TransitOptions(), manifest, payloads,
            notificationOptions: null, keyHeader);
        Assert.That(uploadResp.IsSuccessStatusCode, Is.True);
        var uploadResult = uploadResp.Content!;

        // Payload
        var payloadResp = await cdn.Drives.Reader.GetPayloadAsync(uploadResult.DriveId, uploadResult.FileId, payload.Key);
        Assert.That(payloadResp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {payloadResp.StatusCode}");

        var expectedPayload = uploadedPayloads.Single(p => p.Key == payload.Key);
        var actualBytes = await payloadResp.Content!.ReadAsByteArrayAsync();
        Assert.That(actualBytes.ToBase64(), Is.EqualTo(expectedPayload.EncryptedContent64));

        Assert.That(payloadResp.Headers!.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncrypted), Is.True);
        Assert.That(bool.Parse(isEncrypted!.Single()), Is.True);
        Assert.That(payloadResp.Headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKey), Is.True);
        Assert.That(payloadKey!.Single(), Is.EqualTo(payload.Key));
        Assert.That(payloadResp.Headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentType), Is.True);
        Assert.That(contentType!.Single(), Is.EqualTo(payload.ContentType));

        // CDN never includes the shared-secret-encrypted key header.
        payloadResp.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, out var ssh);
        Assert.That(ssh == null || ssh.All(string.IsNullOrEmpty), Is.True);

        // Thumbnail
        var thumb = payload.Thumbnails.Single();
        var thumbResp = await cdn.Drives.Reader.GetThumbnailAsync(
            uploadResult.DriveId, uploadResult.FileId, payload.Key,
            thumb.PixelWidth, thumb.PixelHeight, directMatchOnly: true);
        Assert.That(thumbResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var expectedThumb = uploadedThumbs.Single();
        var actualThumbBytes = await thumbResp.Content!.ReadAsByteArrayAsync();
        Assert.That(actualThumbBytes.ToBase64(), Is.EqualTo(expectedThumb.EncryptedContent64));

        thumbResp.Headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, out var thumbSsh);
        Assert.That(thumbSsh == null || thumbSsh.All(string.IsNullOrEmpty), Is.True);
    }

    // -----------------------------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------------------------

    //
    // Denial. Everything above asserts the CDN *can* reach a drive; these assert it cannot.
    // Before AllowCdn existed the CDN reached every drive unconditionally, so there was nothing
    // to deny and no coverage for it - which means a regression that quietly re-enabled every
    // drive would pass every other test in this file.
    //

    [Test]
    public async Task CdnCannotReadPayloadOnDriveWithCdnDisabled()
    {
        var owner = await LoginAsOwner(Identities.Sam);
        var cdn = CdnSession.Setup(Host, Identities.Sam);

        // Secured, not anonymous. On an anonymous drive the payload is world-readable and the
        // CDN's drive grant is never consulted, so AllowCdn would have nothing to gate - see
        // CdnStillReadsAnonymousDriveEvenWithCdnDisabled below.
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "cdn toggle drive", allowAnonymousReads: false, allowCdn: true);

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Authenticated);
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var file = await UploadFile(owner, drive, metadata, payload);

        // Baseline: reachable while enabled, so the assertion below is about the flag and not
        // about the upload or the drive being broken in some other way.
        var before = await cdn.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);
        Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {before.StatusCode}");

        await owner.Admin.SetAllowCdn(drive, false);

        var after = await cdn.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "the CDN must lose access to a drive once AllowCdn is turned off");

        // And back on again - the switch has to work in both directions.
        await owner.Admin.SetAllowCdn(drive, true);
        var again = await cdn.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);
        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {again.StatusCode}");
    }

    [Test]
    public async Task CdnSeesOnlyTheEnabledSubsetOfDrives()
    {
        var owner = await LoginAsOwner(Identities.Sam);
        var cdn = CdnSession.Setup(Host, Identities.Sam);

        // Three drives, middle one disabled. Guards the filter in GetCdnEnabledDrivesAsync:
        // a predicate that ignored the flag, or inverted it, fails here.
        var driveA = TargetDrive.NewTargetDrive();
        var driveB = TargetDrive.NewTargetDrive();
        var driveC = TargetDrive.NewTargetDrive();

        var files = new List<(TargetDrive Drive, CreateFileResult File, TestPayloadDefinition Payload)>();
        foreach (var (drive, name) in new[] { (driveA, "cdn a"), (driveB, "cdn b"), (driveC, "cdn c") })
        {
            await owner.Admin.CreateDrive(drive, name, allowAnonymousReads: false, allowCdn: true);
            var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Authenticated);
            var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
            files.Add((drive, await UploadFile(owner, drive, metadata, payload), payload));
        }

        await owner.Admin.SetAllowCdn(driveB, false);

        foreach (var (drive, file, payload) in files)
        {
            var expected = drive == driveB ? "denied" : "allowed";
            var resp = await cdn.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);

            if (drive == driveB)
            {
                Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
                    $"drive B should be {expected}, got {resp.StatusCode}");
            }
            else
            {
                Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                    $"drive should be {expected}, got {resp.StatusCode}");
            }
        }
    }

    [Test]
    public async Task CdnStillReadsAnonymousDriveEvenWithCdnDisabled()
    {
        var owner = await LoginAsOwner(Identities.Sam);
        var cdn = CdnSession.Setup(Host, Identities.Sam);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "public drive", allowAnonymousReads: true);

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var file = await UploadFile(owner, drive, metadata, payload);

        await owner.Admin.SetAllowCdn(drive, false);

        // Documents a real limit of the flag rather than asserting desired behaviour: AllowCdn
        // gates the CDN's *drive grant*, and an anonymous drive's payloads need no grant - they
        // are readable by anyone on the internet, CDN or not. So unchecking the box on a public
        // drive does not stop CDN reads. Not a hole (the content is already public), but it will
        // surprise an owner who expects the checkbox to mean "never cache this".
        var resp = await cdn.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "an anonymous drive stays readable regardless of AllowCdn; change this test only if " +
            "that behaviour is deliberately changed");
    }

    [Test]
    public async Task CdnAuthFailsWhenNoDriveIsCdnEnabled()
    {
        var owner = await LoginAsOwner(Identities.Sam);
        var cdn = CdnSession.Setup(Host, Identities.Sam);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "last cdn drive", allowAnonymousReads: true, allowCdn: true);

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var file = await UploadFile(owner, drive, metadata, payload);

        // System drives are CDN-enabled by default too, so emptying the set means turning every
        // drive off. SetDriveAllowCdnAsync deliberately has no system-drive guard, which is what
        // makes this reachable at all. Exercises the "no drives -> AuthHandlerResult.Fail()"
        // branch in CdnAuthPathHandler.CreateAuthResult.
        foreach (var d in await owner.Admin.GetDrives())
        {
            await owner.Admin.SetAllowCdn(d.TargetDriveInfo, false);
        }

        var resp = await cdn.Drives.Reader.GetPayloadAsync(file.DriveId, file.FileId, payload.Key);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), $"actual {resp.StatusCode}");
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

    private static void AssertPlaintextPayloadHeaders(System.Net.Http.Headers.HttpResponseHeaders headers, TestPayloadDefinition payload)
    {
        Assert.That(headers.TryGetValues(HttpHeaderConstants.PayloadEncrypted, out var isEncrypted), Is.True);
        Assert.That(bool.Parse(isEncrypted!.Single()), Is.False);
        Assert.That(headers.TryGetValues(HttpHeaderConstants.PayloadKey, out var payloadKey), Is.True);
        Assert.That(payloadKey!.Single(), Is.EqualTo(payload.Key));
        Assert.That(headers.TryGetValues(HttpHeaderConstants.DecryptedContentType, out var contentType), Is.True);
        Assert.That(contentType!.Single(), Is.EqualTo(payload.ContentType));

        // CDN auth never carries shared-secret encryption.
        headers.TryGetValues(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, out var ssh);
        Assert.That(ssh == null || ssh.All(string.IsNullOrEmpty), Is.True,
            "SharedSecretEncryptedHeader64 should be absent/empty for a CDN payload response");
    }
}
