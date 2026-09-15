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

namespace Odin.Hosting.Tests.V2.Ported.Ttl;

/// <summary>
/// The anonymous expire-now endpoint: whoever holds a capability link may bring the file's death
/// forward, because they already hold the content - the only thing they can surrender is remaining
/// lifetime. The two guards pinned here: a file with no Ttl can never be destroyed this way, and a
/// file the caller cannot read cannot be destroyed either.
///
/// SECURITY DEBT (see the banner on the endpoint): once BlockAnonymousEnumeration exists, expire-now
/// must additionally require it on the drive, and a test here must pin the refusal on enumerable
/// drives.
/// </summary>
[TestFixture]
public class ExpireNowTests : V2Fixture
{
    [Test]
    public async Task AnonymousHolderOfTheLinkCanExpireATtldFileNow()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "expire-now drive", allowAnonymousReads: true);

        var uid = Guid.NewGuid();
        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        metadata.AppData.UniqueId = uid;
        metadata.Ttl = FileTtl.After(TimeSpan.FromHours(1));

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var file = await UploadFile(owner, drive, metadata, payload);

        using var anon = Host.CreateClient();
        var baseUrl = $"https://{Identities.Frodo}/api/v2/drives/{file.DriveId}/files/by-uid/{uid}";

        var alive = await anon.GetAsync($"{baseUrl}/payload/{payload.Key}");
        Assert.That(alive.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the link must work before it is destroyed");

        var expire = await anon.PostAsync($"{baseUrl}/expire-now", null);
        Assert.That(expire.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the holder of the link must be able to destroy it");

        var dead = await anon.GetAsync($"{baseUrl}/payload/{payload.Key}");
        Assert.That(dead.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "after expire-now the payload must be gone");
    }

    [Test]
    public async Task ExpireNowKillsAnUnopenedBurnFileWithoutStartingItsClockFirst()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "expire-now drive", allowAnonymousReads: true);

        var uid = Guid.NewGuid();
        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        metadata.AppData.UniqueId = uid;
        metadata.Ttl = FileTtl.AfterFirstRead(TimeSpan.FromMinutes(20));

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var file = await UploadFile(owner, drive, metadata, payload);

        using var anon = Host.CreateClient();
        var baseUrl = $"https://{Identities.Frodo}/api/v2/drives/{file.DriveId}/files/by-uid/{uid}";

        // No payload read first: destroying an unopened burn file must not require opening it.
        var expire = await anon.PostAsync($"{baseUrl}/expire-now", null);
        Assert.That(expire.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var dead = await anon.GetAsync($"{baseUrl}/payload/{payload.Key}");
        Assert.That(dead.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "a hastened burn file must not be readable");
    }

    [Test]
    public async Task AFileWithNoTtlCanNeverBeExpiredEarly()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "expire-now drive", allowAnonymousReads: true);

        var uid = Guid.NewGuid();
        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        metadata.AppData.UniqueId = uid;
        metadata.Ttl = FileTtl.Never;

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var file = await UploadFile(owner, drive, metadata, payload);

        using var anon = Host.CreateClient();
        var baseUrl = $"https://{Identities.Frodo}/api/v2/drives/{file.DriveId}/files/by-uid/{uid}";

        var expire = await anon.PostAsync($"{baseUrl}/expire-now", null);
        Assert.That(expire.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "hastening is not killing - permanent public data must be refused outright");

        var alive = await anon.GetAsync($"{baseUrl}/payload/{payload.Key}");
        Assert.That(alive.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the refused file must be untouched");
    }

    [Test]
    public async Task AFileTheCallerCannotReadCannotBeExpired()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "expire-now drive", allowAnonymousReads: true);

        var uid = Guid.NewGuid();
        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.OwnerOnly);
        metadata.AppData.UniqueId = uid;
        metadata.Ttl = FileTtl.After(TimeSpan.FromHours(1));

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var file = await UploadFile(owner, drive, metadata, payload);

        using var anon = Host.CreateClient();
        var baseUrl = $"https://{Identities.Frodo}/api/v2/drives/{file.DriveId}/files/by-uid/{uid}";

        // The by-uid READ path answers 403 for an existing-but-unreadable file; expire-now must be
        // refused the same way, and before anything is touched.
        var read = await anon.GetAsync($"{baseUrl}/header");
        var expire = await anon.PostAsync($"{baseUrl}/expire-now", null);
        Assert.That(expire.StatusCode, Is.EqualTo(read.StatusCode),
            "expire-now must refuse an unreadable file exactly as the read path does");
        Assert.That(expire.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        var stillThere = await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId);
        Assert.That(FileTtl.HasExpired(stillThere.Content!.FileMetadata.Ttl, Odin.Core.Time.UnixTimeUtc.Now()), Is.False,
            "the anonymous attempt must not have touched the owner's file");
    }

    [Test]
    public async Task ExpireNowOnAnUnknownUidIs404()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "expire-now drive", allowAnonymousReads: true);
        var driveId = drive.Alias;

        using var anon = Host.CreateClient();
        var expire = await anon.PostAsync(
            $"https://{Identities.Frodo}/api/v2/drives/{driveId}/files/by-uid/{Guid.NewGuid()}/expire-now", null);
        Assert.That(expire.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
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
