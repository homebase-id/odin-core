using System;
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
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.UnifiedV2.Drive.Read;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Refit;

namespace Odin.Hosting.Tests.V2.Addressing;

/// <summary>
/// Reading another identity's drive by slug: <c>/api/v2/peer/{odinId}/apps/{appSlug}/drives/{driveSlug}/…</c>
/// (docs/drive-addressing.md).  This is the case the addressing columns exist for -- Frodo reaches
/// Sam's drive knowing only the names Sam registered it under, sharing no guid constants with him.
/// </summary>
/// <remarks>
/// Every test asserts the slug route agrees with the guid route on the same drive.  A slug form that
/// returns *something* is not the bar; it has to return the same thing, or the two addresses name
/// different drives and only one of them is right.
/// </remarks>
[TestFixture]
public class PeerAppDriveReadTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    // Not a built-in slug: registration is first-come, and BuiltinApps already holds chat, mail,
    // feed, vault and the rest on every identity.
    private const string AppSlug = "ledger";
    private const string DriveSlug = "records";
    private const string PayloadKey = "pknt0001";

    /// <summary>
    /// Sam registers an app, creates a drive under it, and connects Frodo with Read on it.
    /// </summary>
    /// <remarks>
    /// Sam owns and uploads to the drive; Frodo only reads.  That is deliberately the simpler of the
    /// two shapes -- a peer transfer would exercise the outbox as well, and this suite is about the
    /// address, not the transfer.  Frodo's side has no drive at all, which also proves resolution
    /// happens on Sam's identity: a slug on Frodo's own drive could not stand in for it.
    /// </remarks>
    private async Task<(OwnerSession frodo, OwnerSession sam, TargetDrive drive)> SetupAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var appId = Guid.NewGuid();
        var drive = TargetDrive.NewTargetDrive();

        await sam.Admin.RegisterApp(appId, new PermissionSetGrantRequest(), appSlug: AppSlug);
        await sam.Admin.CreateDrive(drive, "Sam's records", allowAnonymousReads: false, appId: appId,
            driveSlug: DriveSlug, driveTypeSlug: "record");

        // Frodo is the "sender" here only in PeerFlow's naming: the argument order grants him access
        // on Sam's copy, which is what a reader needs.
        await frodo.Admin.CreateDrive(drive, "Frodo's copy", allowAnonymousReads: false);
        await PeerFlow.ConnectAsync(frodo, sam, drive, DrivePermission.Read);

        return (frodo, sam, drive);
    }

    private static IPeerAppDriveHttpClientApiV2 SlugClient(OwnerSession owner)
    {
        var (client, ss) = owner.NewAdminHttpClient();
        return RefitCreator.RestServiceFor<IPeerAppDriveHttpClientApiV2>(client, ss);
    }

    /// <summary>A payload with a thumbnail, keyed so the payload and thumbnail routes can name it.</summary>
    private static TestPayloadDefinition PayloadDefinition()
    {
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        payload.Key = PayloadKey;
        return payload;
    }

    /// <summary>
    /// Sam uploads a file to his own drive and we read its ids back from his stored header, which is
    /// where the GlobalTransitId is reliably populated.
    /// </summary>
    private static async Task<(Guid gtid, Guid samFileId, Guid uniqueId)> SamUploadsAsync(
        OwnerSession sam, TargetDrive drive, bool withPayload)
    {
        var uniqueId = Guid.NewGuid();
        var metadata = SampleMetadataData.Create(fileType: 7001, acl: AccessControlList.Connected);
        metadata.AppData.UniqueId = uniqueId;

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
        var samFileId = upload.Content!.FileId;

        var header = await sam.Drives.Reader.GetFileHeaderAsync(drive.Alias, samFileId);
        Assert.That(header.IsSuccessStatusCode, Is.True, $"sam header read failed: {header.StatusCode}");
        var gtid = header.Content!.FileMetadata.GlobalTransitId
                   ?? throw new InvalidOperationException("stored file should carry a GlobalTransitId");

        return (gtid, samFileId, uniqueId);
    }

    // ---------------------------------------------------------------------------------------------
    // query-batch
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task QueryBatchBySlugFindsWhatWasSent()
    {
        var (frodo, sam, drive) = await SetupAsync();
        await SamUploadsAsync(sam, drive, withPayload: false);

        var request = new QueryBatchRequestV2
        {
            QueryParams = new FileQueryParams { FileType = [7001] },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };

        var response = await SlugClient(frodo).QueryBatch(sam.Identity, AppSlug, DriveSlug, request);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That(response.Content!.SearchResults.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task QueryBatchBySlugAgreesWithTheGuidRoute()
    {
        var (frodo, sam, drive) = await SetupAsync();
        await SamUploadsAsync(sam, drive, withPayload: false);

        var request = new QueryBatchRequestV2
        {
            QueryParams = new FileQueryParams { FileType = [7001] },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };

        var bySlug = await SlugClient(frodo).QueryBatch(sam.Identity, AppSlug, DriveSlug, request);

        // The guid route speaks the V1 wire shape, which carries the TargetDrive inside the params.
        var byGuid = await frodo.Drives.Peer.QueryBatchAsync(sam.Identity, drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = drive, FileType = [7001] },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(bySlug.Content!.SearchResults.Select(r => r.FileId),
            Is.EquivalentTo(byGuid.Content!.SearchResults.Select(r => r.FileId)));
    }

    // ---------------------------------------------------------------------------------------------
    // by GlobalTransitId
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task ExistsByGtidBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var (gtid, _, _) = await SamUploadsAsync(sam, drive, withPayload: false);

        var response = await SlugClient(frodo).GetFileExistsByGtid(sam.Identity, AppSlug, DriveSlug, gtid);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That(response.Content!.Exists, Is.True);
    }

    [Test]
    public async Task ExistsByGtidBySlugIsFalseForAFileNeverSent()
    {
        var (frodo, sam, _) = await SetupAsync();

        var response = await SlugClient(frodo)
            .GetFileExistsByGtid(sam.Identity, AppSlug, DriveSlug, Guid.NewGuid());

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That(response.Content!.Exists, Is.False);
    }

    [Test]
    public async Task GetHeaderByGtidBySlugAgreesWithTheGuidRoute()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var (gtid, samFileId, _) = await SamUploadsAsync(sam, drive, withPayload: false);

        var bySlug = await SlugClient(frodo).GetFileHeaderByGtid(sam.Identity, AppSlug, DriveSlug, gtid);
        var byGuid = await frodo.Drives.Peer.GetFileHeaderByGtidAsync(sam.Identity, drive.Alias, gtid);

        Assert.That(bySlug.Content!.FileId, Is.EqualTo(samFileId));
        Assert.That(bySlug.Content.FileId, Is.EqualTo(byGuid.Content!.FileId));
    }

    [Test]
    public async Task GetPayloadByGtidBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var (gtid, _, _) = await SamUploadsAsync(sam, drive, withPayload: true);

        var response = await SlugClient(frodo)
            .GetPayloadByGtid(sam.Identity, AppSlug, DriveSlug, gtid, PayloadKey);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        var bytes = await response.Content!.ReadAsByteArrayAsync();
        Assert.That(bytes.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetPayloadRangeByGtidBySlugReturnsOnlyTheRequestedBytes()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var (gtid, _, _) = await SamUploadsAsync(sam, drive, withPayload: true);

        var whole = await SlugClient(frodo)
            .GetPayloadByGtid(sam.Identity, AppSlug, DriveSlug, gtid, PayloadKey);
        var wholeBytes = await whole.Content!.ReadAsByteArrayAsync();

        var ranged = await SlugClient(frodo)
            .GetPayloadByGtid(sam.Identity, AppSlug, DriveSlug, gtid, PayloadKey, 0, 8);

        Assert.That(ranged.IsSuccessStatusCode, Is.True, $"got {ranged.StatusCode}");
        var rangedBytes = await ranged.Content!.ReadAsByteArrayAsync();
        Assert.That(rangedBytes.Length, Is.EqualTo(8));
        Assert.That(rangedBytes, Is.EqualTo(wholeBytes.Take(8).ToArray()));
    }

    [Test]
    public async Task GetThumbnailByGtidBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var (gtid, _, _) = await SamUploadsAsync(sam, drive, withPayload: true);

        var thumb = PayloadDefinition().Thumbnails.First();
        var response = await SlugClient(frodo).GetThumbnailByGtid(sam.Identity, AppSlug, DriveSlug, gtid,
            PayloadKey, thumb.PixelWidth, thumb.PixelHeight);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        var bytes = await response.Content!.ReadAsByteArrayAsync();
        Assert.That(bytes.Length, Is.GreaterThan(0));
    }

    // ---------------------------------------------------------------------------------------------
    // by FileId  (Sam's file id, not Frodo's)
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task GetHeaderByFileIdBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var (_, samFileId, _) = await SamUploadsAsync(sam, drive, withPayload: false);

        var response = await SlugClient(frodo).GetFileHeader(sam.Identity, AppSlug, DriveSlug, samFileId);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That(response.Content!.FileId, Is.EqualTo(samFileId));
    }

    [Test]
    public async Task GetPayloadByFileIdBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var (_, samFileId, _) = await SamUploadsAsync(sam, drive, withPayload: true);

        var response = await SlugClient(frodo)
            .GetPayload(sam.Identity, AppSlug, DriveSlug, samFileId, PayloadKey);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That((await response.Content!.ReadAsByteArrayAsync()).Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetPayloadRangeByFileIdBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var (_, samFileId, _) = await SamUploadsAsync(sam, drive, withPayload: true);

        var response = await SlugClient(frodo)
            .GetPayload(sam.Identity, AppSlug, DriveSlug, samFileId, PayloadKey, 0, 8);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That((await response.Content!.ReadAsByteArrayAsync()).Length, Is.EqualTo(8));
    }

    [Test]
    public async Task GetThumbnailByFileIdBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var (_, samFileId, _) = await SamUploadsAsync(sam, drive, withPayload: true);

        var thumb = PayloadDefinition().Thumbnails.First();
        var response = await SlugClient(frodo).GetThumbnail(sam.Identity, AppSlug, DriveSlug, samFileId,
            PayloadKey, thumb.PixelWidth, thumb.PixelHeight);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That((await response.Content!.ReadAsByteArrayAsync()).Length, Is.GreaterThan(0));
    }

    // ---------------------------------------------------------------------------------------------
    // by UniqueId
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task ExistsByUidBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var (_, _, uniqueId) = await SamUploadsAsync(sam, drive, withPayload: false);

        var response = await SlugClient(frodo).GetFileExistsByUid(sam.Identity, AppSlug, DriveSlug, uniqueId);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"got {response.StatusCode}");
        Assert.That(response.Content!.Exists, Is.True);
    }

    // ---------------------------------------------------------------------------------------------
    // addressing failures
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task UnknownAppSlugOnTheRemoteIsRejected()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var (gtid, _, _) = await SamUploadsAsync(sam, drive, withPayload: false);

        var response = await SlugClient(frodo)
            .GetFileHeaderByGtid(sam.Identity, "no-such-app", DriveSlug, gtid);

        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task UnknownDriveSlugOnTheRemoteIsRejected()
    {
        var (frodo, sam, drive) = await SetupAsync();
        var (gtid, _, _) = await SamUploadsAsync(sam, drive, withPayload: false);

        var response = await SlugClient(frodo)
            .GetFileHeaderByGtid(sam.Identity, AppSlug, "no-such-drive", gtid);

        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ADriveTheCallerHasNoGrantOnIsNotResolvable()
    {
        // The slug must not become a way around the grant.  Sam owns a second drive under the same
        // app that Frodo was never granted; naming it has to fail the same way a nonexistent one does.
        var (frodo, sam, _) = await SetupAsync();

        var appId = Guid.NewGuid();
        var privateSlug = "private";
        await sam.Admin.RegisterApp(appId, new PermissionSetGrantRequest(), appSlug: "second");
        await sam.Admin.CreateDrive(TargetDrive.NewTargetDrive(), "Sam's private", appId: appId,
            driveSlug: privateSlug, allowAnonymousReads: false);

        var response = await SlugClient(frodo)
            .GetFileHeaderByGtid(sam.Identity, "second", privateSlug, Guid.NewGuid());

        Assert.That(response.IsSuccessStatusCode, Is.False);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "an ungranted drive must be indistinguishable from a nonexistent one");
    }
}
