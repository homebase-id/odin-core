using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests.AppAPI.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/AppAPI/Transit/TransferFileTests.cs
///
/// An app uploading an encrypted file with <c>TransitOptions.Recipients</c>: refused outright without
/// <see cref="PermissionKeys.UseTransitWrite"/>; otherwise queued, delivered, and readable on the
/// recipient by tag or global transit id, payload and thumbnails byte-identical to what went on the
/// wire. Also covers the two lifecycle cases — a transient file, which the sender no longer holds
/// once sent, and a delete that propagates to the recipient by global transit id.
/// </summary>
/// <remarks>
/// This is the fixture in the <c>AppAPI/Transit</c> batch whose arrange had to be rewritten rather
/// than moved: it rests on <c>_scaffold.OldOwnerApi</c> and <c>_scaffold.AppApi</c>, neither of which
/// exists here. Every step is a 1:1 translation and is named below so a reviewer can check it against
/// the original rather than take it on trust.
/// <list type="bullet">
///   <item><description>
///     <c>OldOwnerApi.SetupTestSampleApp(appId, identity, canReadConnections, drive, driveAllowAnonymousReads, canUseTransit)</c>
///     is <see cref="SetupTestSampleAppAsync"/>: create the drive, then register an app holding
///     <see cref="DrivePermission.All"/> on it plus <see cref="PermissionKeys.ReadConnections"/> +
///     <see cref="PermissionKeys.ReadConnectionRequests"/> when <c>canReadConnections</c>, and
///     <see cref="PermissionKeys.UseTransitWrite"/> when <c>canUseTransit</c> — the exact key set
///     <c>AddAppWithAllDrivePermissions</c> builds.
///   </description></item>
///   <item><description>
///     <c>OldOwnerApi.CreateCircleWithDrive</c> is <see cref="CreateCircleWithDriveAsync"/>, and
///     <c>OldOwnerApi.CreateConnection(sender, recipient, CreateConnectionOptions)</c> is
///     <see cref="CreateConnectionAsync"/> — send with <c>CircleIdsGrantedToRecipient</c>, accept with
///     <c>CircleIdsGrantedToSender</c>. Not <c>PeerFlow.ConnectAsync</c>, which makes its own circles.
///   </description></item>
///   <item><description>
///     <c>AppApi.CreateAppAndTransferFile</c> + <c>AppApi.TransferFile</c> is
///     <see cref="CreateAppAndTransferFileAsync"/>, including its inline assertions (file id non-empty,
///     drive valid, one <c>RecipientStatus</c> per recipient, each <c>Enqueued</c>) and its wire shape:
///     metadata forced encrypted, one payload under <c>WebScaffold.PAYLOAD_KEY</c> encrypted with the
///     file key header, a manifest descriptor carrying an unrelated random IV, and — when asked — one
///     300x300 thumbnail encrypted with the same key header. The original supported N recipients; both
///     callers pass one, so this takes one.
///   </description></item>
///   <item><description>
///     <c>AppApi.DeleteFile</c> is <see cref="DeleteFileAsync"/>, with the same four assertions.
///   </description></item>
///   <item><description>
///     Every <c>WaitForEmptyOutbox</c> / <c>ProcessInbox</c> becomes
///     <c>owner.Sync.DrainOutboxAsync()</c> / <c>app.Sync.ProcessInboxAsync(drive)</c> — the V1 calls
///     poll the outbox background service, which the fast host registers but never starts. Inbox
///     processing runs as the recipient's <em>app</em>, as in the original, which reached it through
///     <c>ITransitTestAppHttpClient</c> on the app client.
///   </description></item>
///   <item><description>
///     Reads and the raw multipart uploads still go through the original's own
///     <see cref="IDriveTestHttpClientForApps"/>, bound to the app caller — the <c>_Universal</c>
///     drive client would have changed the request shape the round-trip assertions rest on.
///   </description></item>
///   <item><description>
///     The trailing <c>DisconnectIdentities</c> calls asserted nothing and are dropped; per-test reset
///     covers them.
///   </description></item>
/// </list>
/// <para>
/// Carried oddities, behaviour left exactly as found:
/// <list type="bullet">
///   <item><description>
///     <c>TransitTestUtilsOptions.DisconnectIdentitiesAfterTransfer</c>, which two of these tests set
///     (one true, one false), is <b>never read</b> by any helper in the V1 tree. Nothing is ported for
///     it, because nothing happened for it.
///   </description></item>
///   <item><description>
///     <see cref="FailToTransferWithoutUseTransitPermission"/> asserts with
///     <c>Assert.That(response.StatusCode == HttpStatusCode.Forbidden)</c> — a precomputed bool, so a
///     failure would have printed <c>Expected: True</c>. Written here as a constraint on the status
///     code, which is the same claim in a form that names both sides.
///   </description></item>
///   <item><description>
///     The three bodies whose <c>[Test]</c> attribute is commented out (<c>UpdateThumbnail</c>,
///     <c>UpdateThumbnailWithTransfer</c>, <c>RecipientCanGetReceivedTransferFromDriveAndIsSearchable</c>)
///     are carried verbatim. They have never run and do not run here either.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
[TestFixture]
public class TransferFileTests : V2Fixture
{
    /// <remarks>
    /// Issue #1771: a peer upload whose comment encryption disagrees with its referenced file trips
    /// the S2040 guard in <c>PeerFileWriter.GetTargetAcl</c>, is logged at Error, and is retried by the
    /// outbox — the four sibling fixtures in this folder that provoke it already tolerate exactly this
    /// message. Listed here because of what was measured while porting this batch: under
    /// <c>ParallelScope.Fixtures</c> these events reach a fixture's log store from <em>another</em>
    /// fixture's host. Running the ported <c>AppTransit*</c> fixtures alone is clean over repeated
    /// runs; running one of them beside <see cref="TransitCommentFileRoutingTests"/> reddens tests that
    /// make no peer call at all, with that fixture's error text. So the per-host log isolation
    /// <see cref="V2Fixture.AssertNoErrorLogEvents"/> documents does not hold under load, and this
    /// fixture — which does send over peer — cannot tell its own occurrence from a neighbour's. Remove
    /// when #1771 is resolved; the isolation gap is reported separately.
    /// </remarks>
    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings =>
        ["Referenced filed and metadata payload encryption do not match"];

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry];

    [Test(Description = "")]
    public async Task FailToTransferWithoutUseTransitPermission()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var appId = Guid.NewGuid();
        var targetDrive = TargetDrive.NewTargetDrive();
        var senderApp = await SetupTestSampleAppAsync(sender, appId, targetDrive,
            canReadConnections: true, driveAllowAnonymousReads: true, canUseTransit: false);
        await SetupTestSampleAppAsync(recipient, appId, targetDrive,
            canReadConnections: true, driveAllowAnonymousReads: false, canUseTransit: false);

        var fileTag = Guid.NewGuid();

        var senderCircleId = await CreateCircleWithDriveAsync(sender, "Sender Circle", [], new PermissionedDrive
        {
            Drive = targetDrive,
            Permission = DrivePermission.ReadWrite
        });

        var recipientCircleId = await CreateCircleWithDriveAsync(recipient, "Recipient Circle", [], new PermissionedDrive
        {
            Drive = targetDrive,
            Permission = DrivePermission.ReadWrite
        });

        await CreateConnectionAsync(sender, recipient,
            circleIdsGrantedToRecipient: [senderCircleId],
            circleIdsGrantedToSender: [recipientCircleId]);

        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        var payloadIv = ByteArrayUtil.GetRndByteArray(16);
        var instructionSet = new UploadInstructionSet
        {
            TransferIv = transferIv,
            StorageOptions = new StorageOptions
            {
                Drive = targetDrive,
                OverwriteFileId = null
            },

            //Add recipients so system will try to send it
            TransitOptions = new TransitOptions
            {
                Recipients = [recipient.Identity]
            },
            Manifest = new UploadManifest
            {
                PayloadDescriptors =
                [
                    new UploadManifestPayloadDescriptor
                    {
                        Iv = payloadIv,
                        PayloadKey = WebScaffold.PAYLOAD_KEY
                    }
                ]
            }
        };

        var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
        var instructionStream = new MemoryStream(bytes);

        var client = senderApp.Factory.CreateHttpClient(senderApp.Identity, out var key);
        var descriptor = new UploadFileDescriptor
        {
            EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
            FileMetadata = new UploadFileMetadata
            {
                AllowDistribution = true,
                AppData = new UploadAppFileMetaData
                {
                    Tags = [fileTag],
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    PreviewThumbnail = new ThumbnailContent
                    {
                        PixelHeight = 100,
                        PixelWidth = 100,
                        ContentType = "image/png",
                        Content = keyHeader.EncryptDataAes(TestMedia.PreviewPngThumbnailBytes)
                    }
                },
                IsEncrypted = true,
                AccessControlList = new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Connected }
            },
        };

        var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

        var payloadKeyHeader = new KeyHeader
        {
            Iv = payloadIv,
            AesKey = keyHeader.AesKey
        };

        const string payloadData = "{payload:true, image:'b64 data'}";
        var payloadCipher = payloadKeyHeader.EncryptDataAesAsStream(payloadData);

        var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
        var response = await transitSvc.Upload(
            new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
            new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
            new StreamPart(payloadCipher, WebScaffold.PAYLOAD_KEY, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task TransientFileIsDeletedAfterSending()
    {
        const int someFiletype = 3892;
        var sender = await LoginAsOwner(Identities.Sam);
        var recipient = await LoginAsOwner(Identities.Merry);

        var instructionSet = UploadInstructionSet.WithRecipients(TargetDrive.NewTargetDrive(), recipient.Identity);
        instructionSet.TransitOptions.IsTransient = true;

        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            AppData = new UploadAppFileMetaData
            {
                FileType = someFiletype,
                Content = "this is some content",
            },
            AccessControlList = AccessControlList.Connected
        };

        var ctx = await CreateAppAndTransferFileAsync(sender, recipient, instructionSet, fileMetadata, includeThumbnail: true);

        var sentFile = ctx.UploadResult.File;

        //
        // On recipient identity - see that file was transferred
        //
        var getFileByTypeResponse = await QueryBatchAsync(ctx.RecipientApp,
            FileQueryParamsV1.FromFileType(ctx.TargetDrive, someFiletype),
            QueryBatchResultOptionsRequest.Default);

        Assert.That(getFileByTypeResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getFileByTypeResponse.Content, Is.Not.Null);
        var recipientFileRecord = getFileByTypeResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(recipientFileRecord, Is.Not.Null);

        var recipientFile = new ExternalFileIdentifier
        {
            FileId = recipientFileRecord!.FileId,
            TargetDrive = ctx.TargetDrive
        };

        var sentThumbnail = ctx.Thumbnails.FirstOrDefault();
        Assert.That(sentThumbnail, Is.Not.Null);
        var thumbnailResponse = await GetThumbnailAsync(ctx.RecipientApp, recipientFile,
            sentThumbnail!.PixelWidth, sentThumbnail.PixelHeight, WebScaffold.PAYLOAD_KEY);
        Assert.That(thumbnailResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(thumbnailResponse.Content, Is.Not.Null);

        var payloadResponse = await GetFilePayloadAsync(ctx.RecipientApp, recipientFile);
        Assert.That(payloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(payloadResponse.Content, Is.Not.Null);

        //
        // On sender identity - see that file is not indexed and not available by direct access
        //
        var getSenderFileResponse = await DriveSvc(ctx.SenderApp).GetFileHeaderAsPost(sentFile);
        Assert.That(getSenderFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "Sender should no longer have the file since we used IsTransient");

        var getSenderThumbnailResponse = await GetThumbnailAsync(ctx.SenderApp, sentFile,
            sentThumbnail.PixelWidth, sentThumbnail.PixelHeight, WebScaffold.PAYLOAD_KEY);
        Assert.That(getSenderThumbnailResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var getSenderPayloadResponse = await GetFilePayloadAsync(ctx.SenderApp, sentFile);
        Assert.That(getSenderPayloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CanDeleteFileOnRecipientServerUsingGlobalTransitId()
    {
        //on recipient identity
        //validate: recipient should have same global unique id
        //validate: client file header should have global unique id

        const int someFiletype = 89994;

        var sender = await LoginAsOwner(Identities.Sam);
        var recipient = await LoginAsOwner(Identities.Merry);

        var instructionSet = UploadInstructionSet.WithRecipients(TargetDrive.NewTargetDrive(), recipient.Identity);

        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            AppData = new UploadAppFileMetaData
            {
                FileType = someFiletype,
                Content = "this is some content",
            },
            AccessControlList = AccessControlList.Connected
        };

        // Send the first file
        var sendFileResult = await CreateAppAndTransferFileAsync(sender, recipient, instructionSet, fileMetadata, includeThumbnail: true);

        Assert.That(sendFileResult.UploadResult.GlobalTransitId, Is.Not.Null);
        Assert.That(sendFileResult.UploadResult.GlobalTransitId.GetValueOrDefault(), Is.Not.EqualTo(Guid.Empty));

        var firstFileSent = sendFileResult.UploadResult.File;

        //
        // On recipient identity - see that file was transferred
        //
        var filesByGlobalTransitId = new FileQueryParamsV1
        {
            TargetDrive = sendFileResult.TargetDrive,
            GlobalTransitId = [sendFileResult.UploadResult.GlobalTransitId.GetValueOrDefault()]
        };

        var getFirstFileByGlobalTransitIdResponse = await QueryBatchAsync(sendFileResult.RecipientApp, filesByGlobalTransitId,
            QueryBatchResultOptionsRequest.Default);

        Assert.That(getFirstFileByGlobalTransitIdResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getFirstFileByGlobalTransitIdResponse.Content, Is.Not.Null);
        var recipientFileRecord = getFirstFileByGlobalTransitIdResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(recipientFileRecord, Is.Not.Null);
        Assert.That(recipientFileRecord!.FileMetadata.GlobalTransitId, Is.EqualTo(sendFileResult.UploadResult.GlobalTransitId));
        Assert.That(recipientFileRecord.FileMetadata.AppData.FileType, Is.EqualTo(sendFileResult.UploadFileMetadata.AppData.FileType));

        // Sender should now delete the file
        await DeleteFileAsync(sendFileResult, firstFileSent);

        //
        // sender server: Should still be in index and marked as deleted
        //
        var qbResponse = await QueryBatchAsync(sendFileResult.SenderApp,
            FileQueryParamsV1.FromFileType(sendFileResult.TargetDrive), QueryBatchResultOptionsRequest.Default);
        Assert.That(qbResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(qbResponse.Content, Is.Not.Null);
        var qbDeleteFileEntry = qbResponse.Content!.SearchResults.SingleOrDefault();
        OdinTestAssertions.FileHeaderIsMarkedDeleted(qbDeleteFileEntry, shouldHaveGlobalTransitId: true,
            SecurityGroupType.Connected); //security group should be cause that's how we sent it

        // recipient server: Should still be in index and marked as deleted
        var recipientQbResponse = await QueryBatchAsync(sendFileResult.RecipientApp,
            FileQueryParamsV1.FromFileType(sendFileResult.TargetDrive), QueryBatchResultOptionsRequest.Default);
        Assert.That(recipientQbResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(recipientQbResponse.Content, Is.Not.Null);
        var recipientQbDeleteFileEntry = recipientQbResponse.Content!.SearchResults.SingleOrDefault();
        OdinTestAssertions.FileHeaderIsMarkedDeleted(recipientQbDeleteFileEntry, shouldHaveGlobalTransitId: true);
    }

    // [Test(Description = "Ensures only the original sender of a file with a global unique identifier can make changes")]
    // public async Task WillRejectChangesFromGlobalTransitIdWhenNotFromOriginalSender()
    // {
    //     Assert.Inconclusive("WIP - testing this requires me to hack the server side and set the same global transit id");
    // }

    [Test(Description = "")]
    public Task CanSendTransferAndRecipientCanGetFilesByTag_SendNowAwaitResponse() =>
        SendTransferAndReadBackByTagAsync(WebScaffold.PAYLOAD_KEY);

    [Test(Description = "")]
    public Task CanSendTransferAndRecipientCanGetFilesByTag_Queued() =>
        SendTransferAndReadBackByTagAsync("abc333yc");

    // [Test(Description = "Updates a thumbnail")]
    public void UpdateThumbnail()
    {
        //upload a file with a thumbnail
    }

    // [Test(Description = "Updates a thumbnail; and transfer it")]
    public void UpdateThumbnailWithTransfer()
    {
        //upload a file with a thumbnail

        //transfer the thumbnail

        //upload an updated thumbnail

        //transfer that update

        //NOTE: I think this requires supporting file collaboration in transit where I can send you updates for an existing file
    }

    //[Test(Description = "")]
    public void RecipientCanGetReceivedTransferFromDriveAndIsSearchable()
    {
        Assert.Inconclusive("TODO");
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The body <see cref="CanSendTransferAndRecipientCanGetFilesByTag_SendNowAwaitResponse"/> and
    /// <see cref="CanSendTransferAndRecipientCanGetFilesByTag_Queued"/> share.
    /// </summary>
    /// <remarks>
    /// The two originals were byte-identical apart from the payload key — <c>_SendNowAwaitResponse</c>
    /// used <c>WebScaffold.PAYLOAD_KEY</c>, <c>_Queued</c> a literal <c>"abc333yc"</c> — and the
    /// incidental order in which they built the payload IV and the manifest. Neither name matches what
    /// it does: both send exactly the same way and both wait for the outbox, so "send now await
    /// response" versus "queued" is a distinction the bodies stopped making at some point. Merged
    /// rather than duplicated, with the one real difference passed in; the names, and therefore the
    /// coverage, are unchanged.
    /// </remarks>
    private async Task SendTransferAndReadBackByTagAsync(string payloadKey)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var appId = Guid.NewGuid();
        var targetDrive = TargetDrive.NewTargetDrive();
        var senderApp = await SetupTestSampleAppAsync(sender, appId, targetDrive,
            canReadConnections: true, driveAllowAnonymousReads: true);
        var recipientApp = await SetupTestSampleAppAsync(recipient, appId, targetDrive,
            canReadConnections: true, driveAllowAnonymousReads: false);

        var fileTag = Guid.NewGuid();

        var senderCircleId = await CreateCircleWithDriveAsync(sender, "Sender Circle", [], new PermissionedDrive
        {
            Drive = targetDrive,
            Permission = DrivePermission.ReadWrite
        });

        var recipientCircleId = await CreateCircleWithDriveAsync(recipient, "Recipient Circle", [], new PermissionedDrive
        {
            Drive = targetDrive,
            Permission = DrivePermission.ReadWrite
        });

        await CreateConnectionAsync(sender, recipient,
            circleIdsGrantedToRecipient: [senderCircleId],
            circleIdsGrantedToSender: [recipientCircleId]);

        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        var instructionSet = new UploadInstructionSet
        {
            TransferIv = transferIv,
            StorageOptions = new StorageOptions
            {
                Drive = targetDrive,
                OverwriteFileId = null
            },

            TransitOptions = new TransitOptions
            {
                Recipients = [recipient.Identity]
            },
            Manifest = new UploadManifest()
        };

        var thumbnail1 = new ThumbnailDescriptor
        {
            PixelHeight = 300,
            PixelWidth = 300,
            ContentType = "image/jpeg"
        };
        var thumbnail1CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes300);

        var thumbnail2 = new ThumbnailDescriptor
        {
            PixelHeight = 400,
            PixelWidth = 400,
            ContentType = "image/jpeg",
        };
        var thumbnail2CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes400);

        var uploadClient = senderApp.Factory.CreateHttpClient(senderApp.Identity, out var key);
        var descriptor = new UploadFileDescriptor
        {
            EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref key),
            FileMetadata = new UploadFileMetadata
            {
                AllowDistribution = true,
                AppData = new UploadAppFileMetaData
                {
                    Tags = [fileTag],
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    PreviewThumbnail = new ThumbnailContent
                    {
                        PixelHeight = 100,
                        PixelWidth = 100,
                        ContentType = "image/png",
                        Content = keyHeader.EncryptDataAes(TestMedia.PreviewPngThumbnailBytes)
                    }
                },
                IsEncrypted = true,
                AccessControlList = new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Connected }
            },
        };

        var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

        const string payloadData = "{payload:true, image:'b64 data'}";
        var payloadIv = ByteArrayUtil.GetRndByteArray(16);

        var payloadKeyHeader = new KeyHeader
        {
            Iv = payloadIv,
            AesKey = keyHeader.AesKey
        };

        var payloadCipher = payloadKeyHeader.EncryptDataAesAsStream(payloadData);

        instructionSet.Manifest.PayloadDescriptors.Add(new UploadManifestPayloadDescriptor
        {
            Iv = payloadIv,
            PayloadKey = payloadKey,
            Thumbnails =
            [
                new UploadedManifestThumbnailDescriptor
                {
                    ThumbnailKey = thumbnail1.GetFilename(payloadKey),
                    PixelHeight = thumbnail1.PixelHeight,
                    PixelWidth = thumbnail1.PixelWidth
                },
                new UploadedManifestThumbnailDescriptor
                {
                    ThumbnailKey = thumbnail2.GetFilename(payloadKey),
                    PixelHeight = thumbnail2.PixelHeight,
                    PixelWidth = thumbnail2.PixelWidth
                }
            ]
        });

        var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
        var instructionStream = new MemoryStream(bytes);

        {
            var transitSvc = RestService.For<IDriveTestHttpClientForApps>(uploadClient);
            var response = await transitSvc.Upload(
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                new StreamPart(payloadCipher, payloadKey, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
                new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(payloadKey), thumbnail1.ContentType,
                    Enum.GetName(MultipartUploadParts.Thumbnail)),
                new StreamPart(new MemoryStream(thumbnail2CipherBytes), thumbnail2.GetFilename(payloadKey), thumbnail2.ContentType,
                    Enum.GetName(MultipartUploadParts.Thumbnail)));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content, Is.Not.Null);
            var transferResult = response.Content!;

            Assert.That(transferResult.File, Is.Not.Null);
            Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(transferResult.File.TargetDrive.IsValid(), Is.True);

            foreach (var r in instructionSet.TransitOptions.Recipients)
            {
                Assert.That(transferResult.RecipientStatus.ContainsKey(r), Is.True, $"Could not find matching recipient {r}");
                Assert.That(transferResult.RecipientStatus[r], Is.EqualTo(TransferStatus.Enqueued), $"file was not enqued for {r}");
            }
        }

        await sender.Sync.DrainOutboxAsync();

        {
            //First force transfers to be put into their long term location
            await recipientApp.Sync.ProcessInboxAsync(targetDrive);

            var driveSvc = DriveSvc(recipientApp);

            //lookup the fileId by the fileTag from earlier
            var queryBatchResponse = await driveSvc.GetBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParamsV1
                {
                    TargetDrive = targetDrive,
                    TagsMatchAll = [fileTag]
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest
                {
                    MaxRecords = 1,
                    IncludeMetadataHeader = true
                }
            });

            Assert.That(queryBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(queryBatchResponse.Content, Is.Not.Null);
            Assert.That(queryBatchResponse.Content!.SearchResults.Count(), Is.EqualTo(1));

            var uploadedFile = new ExternalFileIdentifier
            {
                TargetDrive = targetDrive,
                FileId = queryBatchResponse.Content.SearchResults.Single().FileId
            };

            var fileResponse = await driveSvc.GetFileHeaderAsPost(uploadedFile);

            Assert.That(fileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(fileResponse.Content, Is.Not.Null);

            var clientFileHeader = fileResponse.Content!;

            Assert.That(clientFileHeader.FileMetadata, Is.Not.Null);
            Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);

            Assert.That(clientFileHeader.FileMetadata.AppData.Tags, Is.EquivalentTo(descriptor.FileMetadata.AppData.Tags));
            Assert.That(clientFileHeader.FileMetadata.AppData.Content, Is.EqualTo(descriptor.FileMetadata.AppData.Content));
            Assert.That(clientFileHeader.FileMetadata.Payloads.Count, Is.EqualTo(1));

            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

            var ss = recipientApp.Factory.SharedSecret;
            var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss);

            Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
            Assert.That(ByteArrayUtil.EquiByteArrayCompare(decryptedKeyHeader.AesKey.GetKey(), keyHeader.AesKey.GetKey()), Is.True);

            //validate preview thumbnail
            Assert.That(clientFileHeader.FileMetadata.AppData.PreviewThumbnail.ContentType,
                Is.EqualTo(descriptor.FileMetadata.AppData.PreviewThumbnail.ContentType));
            Assert.That(clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelHeight,
                Is.EqualTo(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelHeight));
            Assert.That(clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelWidth,
                Is.EqualTo(descriptor.FileMetadata.AppData.PreviewThumbnail.PixelWidth));
            Assert.That(ByteArrayUtil.EquiByteArrayCompare(descriptor.FileMetadata.AppData.PreviewThumbnail.Content,
                clientFileHeader.FileMetadata.AppData.PreviewThumbnail.Content), Is.True);

            Assert.That(clientFileHeader.FileMetadata.GetPayloadDescriptor(payloadKey).Thumbnails.Count(), Is.EqualTo(2));

            //
            // Get the payload that was uploaded, test it
            //
            var payloadResponse = await driveSvc.GetPayloadAsPost(new GetPayloadRequest { File = uploadedFile, Key = payloadKey });
            Assert.That(payloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(payloadResponse.Content, Is.Not.Null);

            var payloadResponseCipher = await payloadResponse.Content!.ReadAsByteArrayAsync();
            Assert.That(((MemoryStream)payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));

            var aesKey = decryptedKeyHeader.AesKey;
            var decryptedPayloadBytes = AesCbc.Decrypt(
                cipherText: payloadResponseCipher,
                key: aesKey,
                iv: payloadIv);

            var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadData);
            Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

            //
            // Validate additional thumbnails
            //
            var expectedThumbnails = new List<ThumbnailDescriptor> { thumbnail1, thumbnail2 };
            var clientFileHeaderList = clientFileHeader.FileMetadata.GetPayloadDescriptor(payloadKey).Thumbnails.ToList();

            //validate thumbnail 1
            Assert.That(clientFileHeaderList[0].ContentType, Is.EqualTo(expectedThumbnails[0].ContentType));
            Assert.That(clientFileHeaderList[0].PixelWidth, Is.EqualTo(expectedThumbnails[0].PixelWidth));
            Assert.That(clientFileHeaderList[0].PixelHeight, Is.EqualTo(expectedThumbnails[0].PixelHeight));

            var thumbnailResponse1 = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest
            {
                File = uploadedFile,
                Height = thumbnail1.PixelHeight,
                Width = thumbnail1.PixelWidth,
                PayloadKey = payloadKey
            });

            Assert.That(thumbnailResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(thumbnailResponse1.Content, Is.Not.Null);

            var thumbnailResponse1CipherBytes = await thumbnailResponse1.Content!.ReadAsByteArrayAsync();
            Assert.That(ByteArrayUtil.EquiByteArrayCompare(thumbnail1CipherBytes, thumbnailResponse1CipherBytes), Is.True);

            //validate thumbnail 2
            Assert.That(clientFileHeaderList[1].ContentType, Is.EqualTo(expectedThumbnails[1].ContentType));
            Assert.That(clientFileHeaderList[1].PixelWidth, Is.EqualTo(expectedThumbnails[1].PixelWidth));
            Assert.That(clientFileHeaderList[1].PixelHeight, Is.EqualTo(expectedThumbnails[1].PixelHeight));

            var thumbnailResponse2 = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest
            {
                File = uploadedFile,
                Height = thumbnail2.PixelHeight,
                Width = thumbnail2.PixelWidth,
                PayloadKey = payloadKey
            });

            Assert.That(thumbnailResponse2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(thumbnailResponse2.Content, Is.Not.Null);
            var thumbnailResponse2CipherBytes = await thumbnailResponse2.Content!.ReadAsByteArrayAsync();
            Assert.That(ByteArrayUtil.EquiByteArrayCompare(thumbnail2CipherBytes, thumbnailResponse2CipherBytes), Is.True);

            decryptedKeyHeader.AesKey.Wipe();
        }

        keyHeader.AesKey.Wipe();
    }

    // ---------------------------------------------------------------------------------------------
    // The V1 scaffold helpers this fixture rested on, rebuilt against the fast framework.
    // ---------------------------------------------------------------------------------------------

    /// <summary>What was transferred, and the four sessions the assertions then read it back through.</summary>
    private sealed record TransferContext(
        OwnerSession Sender,
        AppSession SenderApp,
        OwnerSession Recipient,
        AppSession RecipientApp,
        TargetDrive TargetDrive,
        UploadResult UploadResult,
        UploadFileMetadata UploadFileMetadata,
        IReadOnlyList<ThumbnailDescriptor> Thumbnails);

    /// <summary>
    /// <c>OwnerApiTestUtils.SetupTestSampleApp</c>: create the drive, then register an app with
    /// <see cref="DrivePermission.All"/> on it and the permission keys the flags imply.
    /// </summary>
    private static async Task<AppSession> SetupTestSampleAppAsync(
        OwnerSession owner,
        Guid appId,
        TargetDrive targetDrive,
        bool canReadConnections = false,
        bool driveAllowAnonymousReads = false,
        bool canUseTransit = true)
    {
        var keys = new List<int>();
        if (canReadConnections)
        {
            keys.Add(PermissionKeys.ReadConnections);
            keys.Add(PermissionKeys.ReadConnectionRequests);
        }

        if (canUseTransit)
        {
            keys.Add(PermissionKeys.UseTransitWrite);
        }

        await owner.Admin.CreateDrive(targetDrive, $"Test Drive name with type {targetDrive.Type}",
            allowAnonymousReads: driveAllowAnonymousReads, ownerOnly: false);

        return await AppSession.SetupAsync(owner, targetDrive, DrivePermission.All, keys, knownAppId: appId);
    }

    /// <summary><c>OwnerApiTestUtils.CreateCircleWithDrive</c>; returns the circle id.</summary>
    private static async Task<Guid> CreateCircleWithDriveAsync(
        OwnerSession owner, string name, IReadOnlyList<int> permissionKeys, PermissionedDrive drive)
    {
        var circleId = Guid.NewGuid();
        var response = await owner.Admin.CreateCircle(circleId, name, new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet([.. permissionKeys]),
            Drives = [new DriveGrantRequest { PermissionedDrive = drive }]
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return circleId;
    }

    /// <summary><c>OwnerApiTestUtils.CreateConnection</c>: send granting one circle set, accept granting the other.</summary>
    private static async Task CreateConnectionAsync(
        OwnerSession sender, OwnerSession recipient, Guid[] circleIdsGrantedToRecipient, Guid[] circleIdsGrantedToSender)
    {
        var sendResponse = await sender.Connections.SendConnectionRequest(recipient.Identity,
            circleIdsGrantedToRecipient.Select(id => (GuidId)id));
        Assert.That(sendResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var acceptResponse = await recipient.Connections.AcceptConnectionRequest(sender.Identity,
            circleIdsGrantedToSender.Select(id => (GuidId)id));
        Assert.That(acceptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>
    /// <c>AppApiTestUtils.CreateAppAndTransferFile</c> for one recipient: apps and circles on both
    /// sides, the connection, the encrypted upload, then the outbox drain and the recipient's inbox.
    /// </summary>
    private static async Task<TransferContext> CreateAppAndTransferFileAsync(
        OwnerSession sender,
        OwnerSession recipient,
        UploadInstructionSet instructionSet,
        UploadFileMetadata fileMetadata,
        bool includeThumbnail,
        bool driveAllowAnonymousReads = false)
    {
        var targetDrive = instructionSet.StorageOptions.Drive;
        var appId = Guid.NewGuid();

        var senderApp = await SetupTestSampleAppAsync(sender, appId, targetDrive,
            canReadConnections: true, driveAllowAnonymousReads: driveAllowAnonymousReads);

        var senderCircleId = await CreateCircleWithDriveAsync(sender, $"Sender ({sender.Identity}) Circle",
            [PermissionKeys.ReadConnections], new PermissionedDrive
            {
                Drive = targetDrive,
                Permission = DrivePermission.ReadWrite
            });

        var recipientApp = await SetupTestSampleAppAsync(recipient, appId, targetDrive, canReadConnections: false);

        var recipientCircleId = await CreateCircleWithDriveAsync(recipient, $"Circle on {recipient.Identity} identity",
            [PermissionKeys.ReadConnections], new PermissionedDrive
            {
                Drive = targetDrive,
                Permission = DrivePermission.ReadWrite
            });

        await CreateConnectionAsync(sender, recipient,
            circleIdsGrantedToRecipient: [senderCircleId],
            circleIdsGrantedToSender: [recipientCircleId]);

        // --- AppApiTestUtils.TransferFile ---

        var keyHeader = KeyHeader.NewRandom16();
        const string payloadData = "{payload:true, image:'b64 data'}";
        const string payloadKey = WebScaffold.PAYLOAD_KEY;

        fileMetadata.IsEncrypted = true;

        var thumbnailParts = new List<StreamPart>();
        var thumbnailsAdded = new List<ThumbnailDescriptor>();
        var thumbs = new List<UploadedManifestThumbnailDescriptor>();

        if (includeThumbnail)
        {
            var thumbnail1 = new ThumbnailDescriptor
            {
                PixelHeight = 300,
                PixelWidth = 300,
                ContentType = "image/jpeg"
            };

            thumbs.Add(new UploadedManifestThumbnailDescriptor
            {
                PixelHeight = thumbnail1.PixelHeight,
                PixelWidth = thumbnail1.PixelWidth,
                ThumbnailKey = thumbnail1.GetFilename(payloadKey)
            });

            var thumbnail1CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes300);
            thumbnailParts.Add(new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(payloadKey),
                thumbnail1.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
            thumbnailsAdded.Add(thumbnail1);
        }

        instructionSet.Manifest.PayloadDescriptors ??= [];
        instructionSet.Manifest.PayloadDescriptors.Add(new UploadManifestPayloadDescriptor
        {
            Iv = ByteArrayUtil.GetRndByteArray(16),
            PayloadKey = payloadKey,
            Thumbnails = thumbs
        });

        var transferIv = instructionSet.TransferIv;
        var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

        var client = senderApp.Factory.CreateHttpClient(senderApp.Identity, out var sharedSecret);

        var descriptor = new UploadFileDescriptor
        {
            EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
            FileMetadata = fileMetadata
        };

        var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);
        var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadData);

        var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
        var response = await transitSvc.Upload(
            new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
            new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
            new StreamPart(payloadCipher, payloadKey, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
            thumbnailParts.ToArray());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        var transferResult = response.Content!;

        Assert.That(transferResult.File, Is.Not.Null);
        Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(transferResult.File.TargetDrive.IsValid(), Is.True);

        var recipients = instructionSet.TransitOptions?.Recipients ?? [];
        Assert.That(transferResult.RecipientStatus.Count, Is.EqualTo(recipients.Count), "expected recipient count does not match");
        foreach (var r in recipients)
        {
            Assert.That(transferResult.RecipientStatus.ContainsKey(r), Is.True, $"Could not find matching recipient {r}");
            Assert.That(transferResult.RecipientStatus[r], Is.EqualTo(TransferStatus.Enqueued));
        }

        await sender.Sync.DrainOutboxAsync();
        await recipientApp.Sync.ProcessInboxAsync(targetDrive);

        keyHeader.AesKey.Wipe();

        return new TransferContext(sender, senderApp, recipient, recipientApp, targetDrive, transferResult, fileMetadata,
            thumbnailsAdded);
    }

    /// <summary><c>AppApiTestUtils.DeleteFile</c> for one recipient, assertions and delivery included.</summary>
    private static async Task DeleteFileAsync(TransferContext ctx, ExternalFileIdentifier fileId)
    {
        var deleteFileResponse = await DriveSvc(ctx.SenderApp).DeleteFile(new DeleteFileRequest
        {
            File = fileId,
            Recipients = [ctx.Recipient.Identity]
        });

        Assert.That(deleteFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var deleteStatus = deleteFileResponse.Content;
        Assert.That(deleteStatus, Is.Not.Null);
        Assert.That(deleteStatus!.LocalFileNotFound, Is.False);
        Assert.That(deleteStatus.RecipientStatus.Count, Is.EqualTo(1));

        foreach (var (key, value) in deleteStatus.RecipientStatus)
        {
            Assert.That(value, Is.EqualTo(DeleteLinkedFileStatus.Enqueued), $"Delete request failed for {key}");
        }

        await ctx.Sender.Sync.DrainOutboxAsync();
        await ctx.RecipientApp.Sync.ProcessInboxAsync(ctx.TargetDrive);
    }

    // The three app-scoped reads the original reached through _scaffold.AppApi.

    private static IDriveTestHttpClientForApps DriveSvc(AppSession app)
    {
        var client = app.Factory.CreateHttpClient(app.Identity, out var sharedSecret);
        return RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, sharedSecret);
    }

    private static Task<ApiResponse<QueryBatchResponse>> QueryBatchAsync(
        AppSession app, FileQueryParamsV1 queryParams, QueryBatchResultOptionsRequest options) =>
        DriveSvc(app).GetBatch(new QueryBatchRequest
        {
            QueryParams = queryParams,
            ResultOptionsRequest = options
        });

    private static Task<ApiResponse<HttpContent>> GetFilePayloadAsync(AppSession app, ExternalFileIdentifier file) =>
        DriveSvc(app).GetPayloadAsPost(new GetPayloadRequest { File = file, Key = WebScaffold.PAYLOAD_KEY });

    private static Task<ApiResponse<HttpContent>> GetThumbnailAsync(
        AppSession app, ExternalFileIdentifier file, int width, int height, string payloadKey) =>
        DriveSvc(app).GetThumbnailAsPost(new GetThumbnailRequest
        {
            File = file,
            Height = height,
            Width = width,
            PayloadKey = payloadKey
        });
}
