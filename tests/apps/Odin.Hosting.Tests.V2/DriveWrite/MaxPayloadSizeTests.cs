#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.Tests.V2.Ported.Transit;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// The host's largest payload (<c>Host:MaxPayloadBytes</c>): one past it is refused as it streams in, from the owner or
/// a peer, and nothing of it is kept.  Tiny here, so a test payload goes past it.
/// </summary>
[TestFixture]
public class MaxPayloadSizeTests : V2Fixture
{
    private const int MaxPayloadBytes = 64;

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?> { ["Host:MaxPayloadBytes"] = MaxPayloadBytes.ToString() };

    [TestCase(MaxPayloadBytes, true, TestName = "APayloadOfTheLargestSizeIsStored")]
    [TestCase(MaxPayloadBytes + 1, false, TestName = "APayloadPastTheLargestSizeIsRefused")]
    public async Task AnUploadsPayloadIsStoredUpToTheLargestSize(int size, bool stored)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.EnsureDrive(drive, "largest payload", allowAnonymousReads: false);

        var payload = PayloadOf(size);
        var upload = await frodo.Drives.Writer.CreateNewUnencryptedFile(drive.Alias,
            SampleMetadataData.Create(fileType: 100, acl: AccessControlList.OwnerOnly),
            new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] }, [payload]);

        if (stored)
        {
            Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        }
        else
        {
            DriveAsserts.AssertRejected(upload, OdinClientErrorCode.MaxContentLengthExceeded);
        }
    }

    [Test]
    public async Task AThumbnailPastTheLargestThumbnailSizeIsRefusedAsItStreamsIn()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.EnsureDrive(drive, "largest thumbnail", allowAnonymousReads: false);

        var payload = PayloadOf(MaxPayloadBytes);
        payload.Thumbnails = [new ThumbnailContent
        {
            PixelWidth = 200, PixelHeight = 200, ContentType = "image/png",
            Content = ByteArrayUtil.GetRndByteArray(ThumbnailDescriptor.MaxThumbnailSize + 1)
        }];
        var upload = await frodo.Drives.Writer.CreateNewUnencryptedFile(drive.Alias,
            SampleMetadataData.Create(fileType: 100, acl: AccessControlList.OwnerOnly),
            new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] }, [payload]);

        DriveAsserts.AssertRejected(upload, OdinClientErrorCode.MaxContentLengthExceeded);
    }

    /// <summary>
    /// The sender's stored copy grows past the recipient's largest payload after its own server took it, so the
    /// recipient's server is the one to refuse it: written at once (unencrypted), or, encrypted on a drive the sender
    /// holds no key to, straight to long-term storage for the inbox.
    /// </summary>
    [TestCase(false, TestName = "APeersPayloadPastTheLargestSizeIsRefusedAndNothingIsKept")]
    [TestCase(true, TestName = "APeersEncryptedPayloadPastTheLargestSizeIsRefusedAndNothingIsKept")]
    public async Task APeersPayloadPastTheLargestSizeIsRefused(bool encrypted)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Write, "largest payload",
            allowAnonymousReads: !encrypted);

        var payload = PayloadOf(32); // under the largest, encrypted too
        if (encrypted)
        {
            payload.Iv = ByteArrayUtil.GetRndByteArray(16);
        }

        var metadata = SampleMetadataData.CreateWithContent(fileType: 100, "a file with a payload", AccessControlList.Connected);
        metadata.AllowDistribution = true;
        var manifest = new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] };
        var transit = new TransitOptions { Recipients = [sam.Identity] };
        var sent = encrypted
            ? (await frodo.Drives.Writer.CreateEncryptedFile(drive.Alias, metadata, transit, manifest, [payload])).response
            : await frodo.Drives.Writer.CreateNewUnencryptedFile(drive.Alias, metadata, manifest, [payload], transit);
        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), sent.Error?.Content);

        // One byte more than the largest payload in Frodo's stored copy
        await File.WriteAllBytesAsync(await StoredPayloadPathAsync(frodo, drive, sent.Content!.FileId, payload.Key),
            ByteArrayUtil.GetRndByteArray(MaxPayloadBytes + 1));
        await PeerFlow.DistributeAsync(frodo, sam, drive);

        await DriveAsserts.AssertTransferStatus(frodo, new ExternalFileIdentifier { FileId = sent.Content.FileId, TargetDrive = drive },
            sam.Identity, LatestTransferStatus.RecipientIdentityReturnedBadRequest);
        var received = await TransitScenario.QueryByGlobalTransitIdAsync(sam,
            new GlobalTransitIdFileIdentifier { GlobalTransitId = sent.Content.GlobalTransitId!.Value, TargetDrive = drive });
        Assert.That(received, Is.Empty, "no file on Sam's drive");
        AssertNoPayloadFiles(sam, drive);
    }

    private static TestPayloadDefinition PayloadOf(int size)
    {
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        payload.Content = ByteArrayUtil.GetRndByteArray(size);
        return payload;
    }
}
