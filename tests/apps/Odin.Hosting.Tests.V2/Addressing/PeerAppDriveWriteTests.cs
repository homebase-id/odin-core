using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Apps;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Core.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests.V2.Addressing;

/// <summary>
/// Writing to another identity's drive by slug:
/// <c>POST /api/v2/peer/{odinId}/apps/{appSlug}/drives/{driveSlug}/files/{send,senddeleterequest}</c>.
/// This is the case docs/drive-addressing.md leads with -- sending to someone's chat drive without
/// both sides sharing hardcoded guid constants.
/// </summary>
/// <remarks>
/// The guid twin ignores its route: remote drive and recipients both come from the multipart
/// <c>instructions</c> part.  Here the path is the address, so those two body fields are overridden.
/// That override is the thing most worth pinning down, since a caller who does not know about it will
/// send a body that quietly does not matter.
/// </remarks>
[TestFixture]
public class PeerAppDriveWriteTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private const string AppSlug = "ledger";
    private const string DriveSlug = "inbox";

    private async Task<(OwnerSession frodo, OwnerSession sam, TargetDrive drive)> SetupAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var appId = Guid.NewGuid();
        var drive = TargetDrive.NewTargetDrive();

        await sam.Admin.RegisterApp(appId, new PermissionSetGrantRequest(), appSlug: AppSlug);
        await sam.Admin.CreateDrive(drive, "Sam's inbox", allowAnonymousReads: false, appId: appId,
            driveSlug: DriveSlug, driveTypeSlug: "inbox");
        await frodo.Admin.CreateDrive(drive, "Frodo's copy", allowAnonymousReads: false);

        // Frodo gets read+write on Sam's copy: write to send, read to check what landed.
        await PeerFlow.ConnectAsync(frodo, sam, drive, DrivePermission.ReadWrite);

        return (frodo, sam, drive);
    }

    /// <summary>
    /// Built without the shared-secret wrapper on purpose: the write routes are
    /// <c>[NoSharedSecretOnRequest]</c> -- a multipart send cannot be body-encrypted -- so a client
    /// that encrypted would hand the server a payload it never decrypts, and the instructions part
    /// would arrive as noise.  Matches how the guid twin's client is built.
    /// </summary>
    private static IPeerAppDriveHttpClientApiV2 SlugClient(OwnerSession owner)
    {
        var (client, _) = owner.NewAdminHttpClient();
        return RestService.For<IPeerAppDriveHttpClientApiV2>(client);
    }

    /// <summary>
    /// The multipart bundle a peer send takes.  <paramref name="bodyTargetDrive"/> and
    /// <paramref name="bodyRecipients"/> are what goes into the instructions part -- tests pass
    /// deliberately wrong values to prove the route wins.
    /// </summary>
    private static StreamPart[] BuildSendParts(
        OwnerSession sender,
        TargetDrive bodyTargetDrive,
        List<string> bodyRecipients,
        int fileType)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        var instructionSet = new TransitInstructionSet
        {
            TransferIv = transferIv,
            Recipients = bodyRecipients,
            RemoteTargetDrive = bodyTargetDrive
        };

        var (_, sharedSecret) = sender.NewAdminHttpClient();
        var ss = sharedSecret;

        var metadata = SampleMetadataData.Create(fileType: fileType, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;
        metadata.IsEncrypted = false;

        var descriptor = new UploadFileDescriptor
        {
            EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref ss),
            FileMetadata = metadata
        };

        var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());
        var descriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref ss);

        keyHeader.AesKey.Wipe();

        return
        [
            new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                Enum.GetName(MultipartUploadParts.Instructions)),
            new StreamPart(descriptorCipher, "fileDescriptor.encrypted", "application/json",
                Enum.GetName(MultipartUploadParts.Metadata))
        ];
    }

    private static async Task<int> CountOnSamAsync(OwnerSession sam, TargetDrive drive, int fileType)
    {
        return (await QueryOnSamAsync(sam, drive, fileType)).Count;
    }

    private static async Task<SharedSecretEncryptedFileHeader> QueryByGtidAsync(
        OwnerSession sam, TargetDrive drive, Guid gtid)
    {
        var results = await sam.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = drive, GlobalTransitId = [gtid] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        });

        Assert.That(results.IsSuccessStatusCode, Is.True, $"query failed: {results.StatusCode}");
        var hit = results.Content!.SearchResults.SingleOrDefault();
        Assert.That(hit, Is.Not.Null, $"sam should still hold a copy of GTID {gtid}");
        return hit!;
    }

    private static async Task<List<SharedSecretEncryptedFileHeader>> QueryOnSamAsync(
        OwnerSession sam, TargetDrive drive, int fileType)
    {
        var results = await sam.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = drive, FileType = [fileType] },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });
        Assert.That(results.IsSuccessStatusCode, Is.True, $"query failed: {results.StatusCode}");
        return results.Content!.SearchResults.ToList();
    }

    [Test]
    public async Task SendsAFileToADriveNamedBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        const int fileType = 7201;

        var parts = BuildSendParts(frodo, drive, [sam.Identity], fileType);
        var response = await SlugClient(frodo).SendFile(sam.Identity, AppSlug, DriveSlug, parts);

        Assert.That(response.IsSuccessStatusCode, Is.True,
            $"send failed: {response.StatusCode} :: {response.Error?.Content}");

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        Assert.That(await CountOnSamAsync(sam, drive, fileType), Is.EqualTo(1),
            "the file must land on the drive the slug named");
    }

    [Test]
    public async Task TheRouteOverridesTheBodysRemoteTargetDrive()
    {
        // A body naming a different drive must not win: the URL is the more specific statement of
        // intent, and a caller who does not know that would otherwise send somewhere unintended.
        var (frodo, sam, drive) = await SetupAsync();
        const int fileType = 7202;

        var wrongDrive = TargetDrive.NewTargetDrive();
        var parts = BuildSendParts(frodo, wrongDrive, [sam.Identity], fileType);

        var response = await SlugClient(frodo).SendFile(sam.Identity, AppSlug, DriveSlug, parts);

        Assert.That(response.IsSuccessStatusCode, Is.True,
            $"send failed: {response.StatusCode} :: {response.Error?.Content}");

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        Assert.That(await CountOnSamAsync(sam, drive, fileType), Is.EqualTo(1),
            "the slug in the path must decide the drive, not the body");
    }

    [Test]
    public async Task TheRouteOverridesTheBodysRecipients()
    {
        // Same for recipients: {odinId} names exactly one, so an empty or wrong body list is ignored
        // rather than causing a send to nobody.
        var (frodo, sam, drive) = await SetupAsync();
        const int fileType = 7203;

        var parts = BuildSendParts(frodo, drive, [], fileType);
        var response = await SlugClient(frodo).SendFile(sam.Identity, AppSlug, DriveSlug, parts);

        Assert.That(response.IsSuccessStatusCode, Is.True,
            $"send failed: {response.StatusCode} :: {response.Error?.Content}");

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        Assert.That(await CountOnSamAsync(sam, drive, fileType), Is.EqualTo(1),
            "the odinId in the path must decide the recipient, not the body");
    }

    [Test]
    public async Task SendToAnUnknownDriveSlugIsRejected()
    {
        var (frodo, sam, drive) = await SetupAsync();

        var parts = BuildSendParts(frodo, drive, [sam.Identity], 7204);
        var response = await SlugClient(frodo).SendFile(sam.Identity, AppSlug, "no-such-drive", parts);

        Assert.That(response.IsSuccessStatusCode, Is.False,
            "an address that names nothing must not fall back to the body's drive");
    }

    [Test]
    public async Task SendToAnUnknownAppSlugIsRejected()
    {
        var (frodo, sam, drive) = await SetupAsync();

        var parts = BuildSendParts(frodo, drive, [sam.Identity], 7205);
        var response = await SlugClient(frodo).SendFile(sam.Identity, "no-such-app", DriveSlug, parts);

        Assert.That(response.IsSuccessStatusCode, Is.False);
    }

    [Test]
    public async Task SendsADeleteRequestForAFileSentBySlug()
    {
        var (frodo, sam, drive) = await SetupAsync();
        const int fileType = 7206;

        var parts = BuildSendParts(frodo, drive, [sam.Identity], fileType);
        var send = await SlugClient(frodo).SendFile(sam.Identity, AppSlug, DriveSlug, parts);
        Assert.That(send.IsSuccessStatusCode, Is.True, $"send failed: {send.StatusCode}");

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);
        // Read the GlobalTransitId back off Sam's stored copy rather than the send response: it is the
        // id the delete has to name, and taking it from where the file actually is keeps this test
        // honest about what landed.
        var landed = await QueryOnSamAsync(sam, drive, fileType);
        Assert.That(landed.Count, Is.EqualTo(1));

        var gtid = landed.Single().FileMetadata.GlobalTransitId
                   ?? throw new InvalidOperationException("a transferred file must carry a GlobalTransitId");

        var request = new DeleteFileByGlobalTransitIdRequest
        {
            GlobalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier
            {
                GlobalTransitId = gtid,

                // Overridden from the route; set to something wrong on purpose.
                TargetDrive = TargetDrive.NewTargetDrive()
            },
            Recipients = [],
            FileSystemType = FileSystemType.Standard
        };

        var delete = await SlugClient(frodo).SendDeleteRequest(sam.Identity, AppSlug, DriveSlug, request);

        Assert.That(delete.IsSuccessStatusCode, Is.True,
            $"delete failed: {delete.StatusCode} :: {delete.Error?.Content}");

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        // A peer delete soft-deletes: Sam's copy stays queryable and flips state, it does not vanish.
        var afterDelete = await QueryByGtidAsync(sam, drive, gtid);
        Assert.That(afterDelete.FileState, Is.EqualTo(FileState.Deleted),
            "the delete must reach the drive the slug named, not the one in the body");
    }
}
