using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.UnifiedV2.Drive.Read;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Refit;

namespace Odin.Hosting.Tests.V2.Addressing;

/// <summary>
/// The temporal (time-boxed) peer routes addressed by slug:
/// <c>/api/v2/peer/{odinId}/apps/{appSlug}/drives/{driveSlug}/temporal/…</c>.
/// </summary>
/// <remarks>
/// Temporal access is granted through <see cref="DrivePermission.ConditionalTemporalRead"/> and the
/// remote clamps every read to a recent window.  These tests only establish that the slug form
/// reaches the same endpoints the guid form does -- <c>TemporalReadTests</c> owns the clamping and
/// notification behaviour, and duplicating it here would be testing the same thing twice.
///
/// <para>The window is set large enough that nothing falls out of it mid-test; the point is the
/// address, not the clock.</para>
/// </remarks>
[TestFixture]
public class PeerAppDriveTemporalTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private const string AppSlug = "ledger";
    private const string DriveSlug = "records";
    private const string PayloadKey = "pknt0001";
    private const int WindowSeconds = 600;

    private async Task<(OwnerSession frodo, OwnerSession sam, TargetDrive drive)> SetupAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var appId = Guid.NewGuid();
        var drive = TargetDrive.NewTargetDrive();

        await sam.Admin.RegisterApp(appId, new PermissionSetGrantRequest(), appSlug: AppSlug);
        await sam.Admin.CreateDrive(drive, "Sam's records", allowAnonymousReads: false,
            attributes: new Dictionary<string, string> { [TemporalRead.MaxAgeAttributeKey] = WindowSeconds.ToString() },
            appId: appId, driveSlug: DriveSlug, driveTypeSlug: "record");

        await frodo.Admin.CreateDrive(drive, "Frodo's copy", allowAnonymousReads: false);
        await PeerFlow.ConnectAsync(frodo, sam, drive, DrivePermission.ConditionalTemporalRead);

        return (frodo, sam, drive);
    }

    private static IPeerAppDriveHttpClientApiV2 SlugClient(OwnerSession owner)
    {
        var (client, ss) = owner.NewAdminHttpClient();
        return RefitCreator.RestServiceFor<IPeerAppDriveHttpClientApiV2>(client, ss);
    }

    private static TestPayloadDefinition PayloadDefinition()
    {
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        payload.Key = PayloadKey;
        return payload;
    }

    private static async Task<Guid> SamUploadsAsync(OwnerSession sam, TargetDrive drive, bool withPayload)
    {
        var metadata = SampleMetadataData.Create(fileType: 7101, acl: AccessControlList.Connected);

        ApiResponse<CreateFileResult> upload;
        if (withPayload)
        {
            var payload = PayloadDefinition();
            var manifest = new UploadManifest
            {
                PayloadDescriptors = new List<TestPayloadDefinition> { payload }.ToPayloadDescriptorList().ToList()
            };
            upload = await sam.Drives.Writer.CreateNewUnencryptedFile(drive.Alias, metadata, manifest, [payload]);
        }
        else
        {
            upload = await sam.Drives.Writer.UploadNewMetadata(drive.Alias, metadata);
        }

        Assert.That(upload.IsSuccessStatusCode, Is.True, $"sam upload failed: {upload.StatusCode}");
        return upload.Content!.FileId;
    }

    [Test]
    public async Task VerifyTemporalAccessBySlug()
    {
        var (frodo, sam, _) = await SetupAsync();

        var response = await SlugClient(frodo).VerifyTemporalAccess(sam.Identity, AppSlug, DriveSlug);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That(response.Content!.HasAccess, Is.True);
        Assert.That(response.Content.WindowSeconds, Is.EqualTo(WindowSeconds));
    }

    [Test]
    public async Task VerifyTemporalAccessBySlugAgreesWithTheGuidRoute()
    {
        var (frodo, sam, drive) = await SetupAsync();

        var bySlug = await SlugClient(frodo).VerifyTemporalAccess(sam.Identity, AppSlug, DriveSlug);
        var byGuid = await frodo.Drives.Peer.VerifyTemporalAccessAsync(sam.Identity, drive.Alias);

        Assert.That(bySlug.Content!.HasAccess, Is.EqualTo(byGuid.Content!.HasAccess));
        Assert.That(bySlug.Content.WindowSeconds, Is.EqualTo(byGuid.Content.WindowSeconds));
    }

    [Test]
    public async Task TemporalQueryBatchBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        await SamUploadsAsync(sam, drive, withPayload: false);

        var request = new QueryBatchRequestV2
        {
            QueryParams = new FileQueryParams { FileType = [7101] },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };

        var response = await SlugClient(frodo).TemporalQueryBatch(sam.Identity, AppSlug, DriveSlug, request);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That(response.Content!.SearchResults.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task TemporalGetFileHeaderBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var fileId = await SamUploadsAsync(sam, drive, withPayload: false);

        var response = await SlugClient(frodo).TemporalGetFileHeader(sam.Identity, AppSlug, DriveSlug, fileId);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That(response.Content!.FileId, Is.EqualTo(fileId));
    }

    [Test]
    public async Task TemporalGetPayloadBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var fileId = await SamUploadsAsync(sam, drive, withPayload: true);

        var response = await SlugClient(frodo)
            .TemporalGetPayload(sam.Identity, AppSlug, DriveSlug, fileId, PayloadKey);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That((await response.Content!.ReadAsByteArrayAsync()).Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task TemporalGetPayloadRangeBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var fileId = await SamUploadsAsync(sam, drive, withPayload: true);

        var response = await SlugClient(frodo)
            .TemporalGetPayload(sam.Identity, AppSlug, DriveSlug, fileId, PayloadKey, 0, 8);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That((await response.Content!.ReadAsByteArrayAsync()).Length, Is.EqualTo(8));
    }

    [Test]
    public async Task TemporalGetThumbnailBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var fileId = await SamUploadsAsync(sam, drive, withPayload: true);

        var thumb = PayloadDefinition().Thumbnails.First();
        var response = await SlugClient(frodo).TemporalGetThumbnail(sam.Identity, AppSlug, DriveSlug, fileId,
            PayloadKey, thumb.PixelWidth, thumb.PixelHeight);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That((await response.Content!.ReadAsByteArrayAsync()).Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task TemporalRoutesRejectAnUnknownAddress()
    {
        var (frodo, sam, _) = await SetupAsync();

        var response = await SlugClient(frodo).VerifyTemporalAccess(sam.Identity, AppSlug, "no-such-drive");

        Assert.That(response.IsSuccessStatusCode, Is.False);
    }
}
