#nullable enable
using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Ported.Transit;
using Odin.Hosting.Tests.V2.ZeroKnowledge;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// Changing a file you sent (docs/file-access.md, "Changing a file you sent"): who may change a file they sent to
/// another identity's drive, and the version tag the change names.
/// </summary>
[TestFixture]
public class ChangingAFileYouSentTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Pippin];

    [Test]
    public async Task AnUpdateFromSomeoneElseThanItsSenderIsRefused()
    {
        // Pippin's encrypted file on Frodo's drive, which Sam may write too: neither holds the drive's key, so their
        // updates go through Frodo's inbox.  Sam's update of it, naming its current version tag, is refused, since only
        // its original sender changes a file on a drive that isn't a collaborative channel; Pippin's lands, naming no tag,
        // as no one else has written it since his
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var pippin = await LoginAsOwner(Identities.Pippin);
        var drive = await SentToFrodoAsync(pippin, frodo);
        await sam.Admin.EnsureDrive(drive, $"{sam.Identity} sent to Frodo", allowAnonymousReads: false);
        await PeerFlow.ConnectAsync(sam, frodo, drive, DrivePermission.Write);

        var sent = await SendAsync(pippin, frodo, drive, "Pippin's");
        var before = await HeaderAsync(frodo, drive, sent.Gtid);
        Assert.That(before.FileMetadata.AppData.Content, Is.EqualTo(sent.Content));

        await UpdateAsync(sam, frodo, OnPeer(drive, sent), before.FileMetadata.VersionTag, "Sam's", sent.FileKey);
        var after = await HeaderAsync(frodo, drive, sent.Gtid);
        Assert.That(after.FileMetadata.AppData.Content, Is.EqualTo(sent.Content), "Sam's update didn't land");
        Assert.That(after.FileMetadata.VersionTag, Is.EqualTo(before.FileMetadata.VersionTag));

        var (changed, _) = await UpdateAsync(pippin, frodo, OnPeer(drive, sent), null, "Pippin's, changed", sent.FileKey);
        Assert.That((await HeaderAsync(frodo, drive, sent.Gtid)).FileMetadata.AppData.Content, Is.EqualTo(changed), "Pippin's did");
    }

    [Test]
    public async Task ASendersUpdateOfTheirOwnCopyReachesTheRecipientUntilTheRecipientChangesIt()
    {
        // As chat edits a message: Pippin updates his own copy and the update goes on to Frodo, naming no version tag of
        // Frodo's copy, which no one else has changed since Pippin's last write.  Frodo archiving it changes none of its
        // content and leaves that so; once Frodo edits his copy, Pippin's next update is refused there
        var frodo = await LoginAsOwner(Identities.Frodo);
        var pippin = await LoginAsOwner(Identities.Pippin);
        var drive = await SentToFrodoAsync(pippin, frodo);

        var sent = await SendAsync(pippin, frodo, drive, "Pippin's");

        var pippinsCopy = new FileIdentifier { FileId = sent.FileId, TargetDrive = drive };
        var (changed, newTag) = await UpdateAsync(pippin, frodo, pippinsCopy, sent.VersionTag, "Pippin's, changed", sent.FileKey);
        var frodos = await HeaderAsync(frodo, drive, sent.Gtid);
        Assert.That(frodos.FileMetadata.AppData.Content, Is.EqualTo(changed), "Pippin's update reached Frodo");
        Assert.That(frodos.FileMetadata.VersionTag, Is.EqualTo(newTag), "with Pippin's new tag");

        var archive = await frodo.Drives.Writer.UpdateArchivalStatusBatch(drive.Alias, new UpdateArchivalStatusBatchRequestV2
        {
            ArchivalStatus = 2,
            Files = [new FileVersionTagV2 { FileId = frodos.FileId, VersionTag = frodos.FileMetadata.VersionTag }]
        });
        Assert.That(archive.StatusCode, Is.EqualTo(HttpStatusCode.OK), archive.Error?.Content);
        (changed, newTag) = await UpdateAsync(pippin, frodo, pippinsCopy, newTag, "Pippin's, after Frodo archived it", sent.FileKey);
        frodos = await HeaderAsync(frodo, drive, sent.Gtid);
        Assert.That(frodos.FileMetadata.AppData.Content, Is.EqualTo(changed), "archiving it didn't end Pippin's");

        var (frodosEdit, _) = await UpdateAsync(frodo, null, new FileIdentifier { FileId = frodos.FileId, TargetDrive = drive },
            frodos.FileMetadata.VersionTag, "Frodo's edit", sent.FileKey);
        await UpdateAsync(pippin, frodo, pippinsCopy, newTag, "Pippin's, after Frodo's", sent.FileKey);
        Assert.That((await HeaderAsync(frodo, drive, sent.Gtid)).FileMetadata.AppData.Content, Is.EqualTo(frodosEdit),
            "refused: Frodo changed it");
    }

    [Test]
    public async Task OnACollaborativeChannelAMemberNamesTheFilesVersionTag()
    {
        // Sam, a member who may read Pippin's post on Frodo's channel, changes it by naming the version tag it has: another
        // tag is refused.  After Sam's change, Pippin, its sender, changes it only by naming its tag too, as he may read it
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var pippin = await LoginAsOwner(Identities.Pippin);
        var drive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await Ported.Peer.CollabChannelFlow.SetupAsync(frodo, drive, [pippin, sam]);
        await pippin.Admin.EnsureDrive(drive, $"{pippin.Identity} channel");
        var sent = await SendAsync(pippin, frodo, drive, "Pippin's");
        var posted = await HeaderAsync(frodo, drive, sent.Gtid);

        await UpdateAsync(sam, frodo, OnPeer(drive, sent), Guid.NewGuid(), "Sam's, another tag", sent.FileKey);
        Assert.That((await HeaderAsync(frodo, drive, sent.Gtid)).FileMetadata.VersionTag, Is.EqualTo(posted.FileMetadata.VersionTag),
            "refused: another tag");
        var (samsChange, _) = await UpdateAsync(sam, frodo, OnPeer(drive, sent), posted.FileMetadata.VersionTag, "Sam's", sent.FileKey);
        var samsVersion = await HeaderAsync(frodo, drive, sent.Gtid);
        Assert.That(samsVersion.FileMetadata.AppData.Content, Is.EqualTo(samsChange), "Sam's, naming its tag");

        await UpdateAsync(pippin, frodo, OnPeer(drive, sent), null, "Pippin's, naming no tag", sent.FileKey);
        Assert.That((await HeaderAsync(frodo, drive, sent.Gtid)).FileMetadata.AppData.Content, Is.EqualTo(samsChange),
            "refused: Sam wrote it since Pippin");
        var (pippinsChange, _) = await UpdateAsync(pippin, frodo, OnPeer(drive, sent), samsVersion.FileMetadata.VersionTag,
            "Pippin's, naming its tag", sent.FileKey);
        Assert.That((await HeaderAsync(frodo, drive, sent.Gtid)).FileMetadata.AppData.Content, Is.EqualTo(pippinsChange));
    }

    /// <summary>A file sent: its global transit id, encrypted content and file key, and the sender's copy and its tag.</summary>
    private record Sent(Guid Gtid, string Content, KeyHeader FileKey, Guid FileId, Guid VersionTag);

    /// <summary>A drive Frodo grants <paramref name="sender"/> Write on, and no storage key.</summary>
    private static Task<TargetDrive> SentToFrodoAsync(OwnerSession sender, OwnerSession frodo) =>
        PeerFlow.CreatePeerDriveAsync(sender, frodo, DrivePermission.Write, "sent to Frodo", allowAnonymousReads: false);

    private static FileIdentifier OnPeer(TargetDrive drive, Sent sent) => new() { GlobalTransitId = sent.Gtid, TargetDrive = drive };

    /// <summary><paramref name="sender"/> sends a new encrypted file with <paramref name="content"/> to <paramref name="recipient"/>.</summary>
    private static async Task<Sent> SendAsync(OwnerSession sender, OwnerSession recipient, TargetDrive drive, string content)
    {
        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;
        metadata.AppData.Content = content;
        var fileKey = KeyHeader.NewRandom16();
        var (send, encrypted) = await sender.V1.Drive.UploadNewEncryptedMetadata(metadata, new StorageOptions { Drive = drive },
            new TransitOptions { Recipients = [recipient.Identity.DomainName] }, fileKey);
        Assert.That(send.IsSuccessStatusCode, Is.True, send.Error?.Content);
        await PeerFlow.DistributeAsync(sender, recipient, drive);
        return new Sent(send.Content!.GlobalTransitId!.Value, encrypted, fileKey, send.Content.File.FileId, send.Content.NewVersionTag);
    }

    /// <summary>
    /// <paramref name="caller"/> updates <paramref name="file"/> with its key and a new IV, naming
    /// <paramref name="versionTag"/>: a file id is their own copy (<see cref="UpdateLocale.Local"/>), sent on to
    /// <paramref name="recipient"/> when given; a global transit id is the recipient's copy only
    /// (<see cref="UpdateLocale.Peer"/>).  The encrypted content and the new version tag.
    /// </summary>
    private static async Task<(string Content, Guid VersionTag)> UpdateAsync(OwnerSession caller, OwnerSession? recipient,
        FileIdentifier file, Guid? versionTag, string content, KeyHeader fileKey)
    {
        var metadata = SampleMetadataData.Create(fileType: 100, acl: recipient == null ? AccessControlList.OwnerOnly : AccessControlList.Connected);
        metadata.AllowDistribution = recipient != null;
        metadata.AppData.Content = content;
        metadata.VersionTag = versionTag;
        var (update, encrypted, _, _) = await caller.V1.Drive.UpdateEncryptedFile(new FileUpdateInstructionSet
        {
            Locale = file.GlobalTransitId.HasValue ? UpdateLocale.Peer : UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = file,
            Recipients = recipient == null ? [] : [recipient.Identity],
            Manifest = new UploadManifest { PayloadDescriptors = [] }
        }, metadata, [], ZkTestFiles.WithNewIv(fileKey));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
        if (recipient != null)
        {
            await PeerFlow.DistributeAsync(caller, recipient, file.TargetDrive);
        }

        return (encrypted, update.Content!.NewVersionTag);
    }

    private static Task<SharedSecretEncryptedFileHeader> HeaderAsync(OwnerSession owner, TargetDrive drive, Guid gtid) =>
        TransitScenario.SingleByGlobalTransitIdAsync(owner, new GlobalTransitIdFileIdentifier { GlobalTransitId = gtid, TargetDrive = drive });
}
