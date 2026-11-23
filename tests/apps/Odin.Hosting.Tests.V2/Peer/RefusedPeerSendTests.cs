#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Odin.Core;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Ported.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Refit;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// A peer update or a transient upload is stored on the sender's transient drive until the outbox sends it, and only the
/// outbox removes it.  One stored and then refused, or that no recipient was queued for, stayed there for good, and its
/// uniqueId refused every later update of the same file.  A send from a caller without <c>UseTransitWrite</c> is refused
/// before anything is stored (#2155); a transient file the outbox didn't get is deleted (#2158).
///
/// Layout: Pippin sends; Frodo hosts the drive, and grants Pippin Write through a circle.  Merry isn't connected to
/// Pippin, so a send to her is never queued.  Sam hosts a second copy of Frodo's drive, for an update sent to both.
/// </summary>
[TestFixture]
public class RefusedPeerSendTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Pippin, Identities.Merry];

    private static readonly Guid TransientDrive = SystemDriveConstants.TransientTempDrive.Alias;
    private static readonly Odin.Core.Identity.OdinId Merry = new(Identities.Merry);

    private static UploadManifest EmptyManifest => new() { PayloadDescriptors = [] };

    /// <summary>How the first attempt fails: refused before anything is stored, or stored and then queued for no recipient</summary>
    public enum FirstTry { RefusedForUseTransitWrite, QueuedForNoOne }

    [TestCase(FirstTry.RefusedForUseTransitWrite)]
    [TestCase(FirstTry.QueuedForNoOne)]
    public async Task APeerUpdateThatIsntSentLeavesNothing_SoTheNextOneGoesThrough(FirstTry firstTry)
    {
        var (pippin, frodo, drive) = await ArrangeAsync();
        var gtid = await V2PeerUpdateByGtidTests.TransferAsync(pippin, frodo, drive, Metadata(Guid.NewGuid(), "first"), []);
        var uniqueId = Guid.NewGuid();
        var instructions = V2PeerUpdateByGtidTests.Instructions(drive, EmptyManifest);

        if (firstTry == FirstTry.RefusedForUseTransitWrite)
        {
            var readOnlyApp = await ReadOnlyAppAsync(pippin, SystemDriveConstants.TransientTempDrive);
            AssertRefusedForSending(await V2PeerUpdateByGtidTests.UpdateAsync(readOnlyApp, frodo.Identity, drive, gtid,
                Metadata(uniqueId, "not sent"), instructions));
        }
        else
        {
            var unqueued = await V2PeerUpdateByGtidTests.UpdateAsync(pippin, Merry, drive, gtid, Metadata(uniqueId, "not sent"), instructions);
            AssertQueuedForNoOne(unqueued, unqueued.Content?.RecipientStatus);
        }

        await AssertNothingStoredAsync(pippin, TransientDrive, uniqueId);

        var sent = await V2PeerUpdateByGtidTests.UpdateAsync(pippin, frodo.Identity, drive, gtid, Metadata(uniqueId, "sent"), instructions);
        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), sent.Error?.Content);

        await pippin.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive, int.MaxValue);
        var header = await frodo.V1.Drive.QueryByGlobalTransitId(new GlobalTransitIdFileIdentifier { GlobalTransitId = gtid, TargetDrive = drive });
        Assert.That(header.Content!.SearchResults.Single().FileMetadata.AppData.Content, Is.EqualTo("sent"));
    }

    [TestCase(FirstTry.RefusedForUseTransitWrite)]
    [TestCase(FirstTry.QueuedForNoOne)]
    public async Task ATransientUploadThatIsntSentLeavesNothing_SoTheNextOneGoesThrough(FirstTry firstTry)
    {
        var (pippin, frodo, drive) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        var transit = new TransitOptions { IsTransient = true, Recipients = [frodo.Identity.DomainName], RemoteTargetDrive = drive };

        if (firstTry == FirstTry.RefusedForUseTransitWrite)
        {
            var readOnlyApp = await ReadOnlyAppAsync(pippin, SystemDriveConstants.TransientTempDrive);
            AssertRefusedForSending(await readOnlyApp.Drives.Writer.CreateNewUnencryptedFile(TransientDrive, Metadata(uniqueId, "not sent"),
                EmptyManifest, [], transit));
        }
        else
        {
            // SendFile logs a recipient it can't queue at Error (PeerOutgoingTransferService.CreateOutboxItems)
            ExpectErrorLogEvent("Failed while creating outbox item");
            var unqueued = await pippin.Drives.Writer.CreateNewUnencryptedFile(TransientDrive, Metadata(uniqueId, "not sent"), EmptyManifest, [],
                new TransitOptions { IsTransient = true, Recipients = [Merry.DomainName], RemoteTargetDrive = drive });
            AssertQueuedForNoOne(unqueued, unqueued.Content?.RecipientStatus);
        }

        await AssertNothingStoredAsync(pippin, TransientDrive, uniqueId);

        var sent = await pippin.Drives.Writer.CreateNewUnencryptedFile(TransientDrive, Metadata(uniqueId, "sent"), EmptyManifest, [], transit);
        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), sent.Error?.Content);

        await pippin.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive, int.MaxValue);
        var delivered = await frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, drive.Alias);
        Assert.That(delivered.StatusCode, Is.EqualTo(HttpStatusCode.OK), delivered.Error?.Content);
        Assert.That(delivered.Content!.FileMetadata.AppData.Content, Is.EqualTo("sent"));
    }

    /// <summary>A local upload that names recipients is refused whole: before, the local file was stored, then the send refused</summary>
    [Test]
    public async Task ARefusedLocalUploadWithRecipientsStoresNothing()
    {
        var pippin = await LoginAsOwner(Identities.Pippin);
        var uniqueId = Guid.NewGuid();
        var localDrive = TargetDrive.NewTargetDrive();
        await pippin.Admin.CreateDrive(localDrive, "local drive");
        var readOnlyApp = await ReadOnlyAppAsync(pippin, localDrive);

        var refused = await readOnlyApp.Drives.Writer.CreateNewUnencryptedFile(localDrive.Alias, Metadata(uniqueId, "refused"),
            EmptyManifest, [], new TransitOptions { Recipients = [Identities.Frodo], RemoteTargetDrive = TargetDrive.NewTargetDrive() });

        AssertRefusedForSending(refused);
        await AssertNothingStoredAsync(pippin, localDrive.Alias, uniqueId);
    }

    /// <summary>A local update that names recipients is refused whole: before, the local file was changed, then the send refused</summary>
    [Test]
    public async Task ARefusedLocalUpdateWithRecipientsChangesNothing()
    {
        var pippin = await LoginAsOwner(Identities.Pippin);
        var uniqueId = Guid.NewGuid();
        var localDrive = TargetDrive.NewTargetDrive();
        await pippin.Admin.CreateDrive(localDrive, "local drive");
        var readOnlyApp = await ReadOnlyAppAsync(pippin, localDrive);
        var created = await readOnlyApp.Drives.Writer.CreateNewUnencryptedFile(localDrive.Alias, Metadata(uniqueId, "first"), EmptyManifest, []);
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), created.Error?.Content);

        var metadata = Metadata(uniqueId, "refused");
        metadata.VersionTag = created.Content!.NewVersionTag;
        var refused = await readOnlyApp.Drives.Writer.UpdateFileByFileId(localDrive.Alias, created.Content.FileId, new FileUpdateInstructionSetV2
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            Recipients = [new Odin.Core.Identity.OdinId(Identities.Frodo)],
            Manifest = EmptyManifest
        }, metadata, []);

        AssertRefusedForSending(refused);
        var stored = await pippin.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, localDrive.Alias);
        Assert.That(stored.Content!.FileMetadata.AppData.Content, Is.EqualTo("first"));
        Assert.That(stored.Content.FileMetadata.VersionTag, Is.EqualTo(created.Content.NewVersionTag));
    }

    /// <summary>
    /// A peer update to two recipients is queued as one item each, on one transient file: it stays until both are sent.
    /// The outbox counted only queued sends, not updates, so it deleted the file once the first was sent, and a second
    /// still waiting (here Sam's, tried again after a 500) had nothing left to send
    /// </summary>
    [Test]
    public async Task APeerUpdateToTwoRecipientsReachesBoth()
    {
        var (pippin, frodo, drive) = await ArrangeAsync();
        var sam = await LoginAsOwner(Identities.Sam);
        await CollabChannelFlow.SetupAsync(sam, drive, [pippin], driveName: "Sam's drive");

        var sent = await pippin.V1.PeerDirect.TransferNewFile(drive, Metadata(Guid.NewGuid(), "first"), [frodo.Identity, sam.Identity], null,
            EmptyManifest, []);
        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), sent.Error?.Content);
        await pippin.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive, int.MaxValue);
        await sam.Sync.ProcessInboxAsync(drive, int.MaxValue);
        var file = sent.Content!.RemoteGlobalTransitIdFileIdentifier;

        // Sam's server answers the first try with a 500, so his update is tried again after Frodo's was sent
        Host.PeerFaults.FailNext(from: Identities.Pippin, to: Identities.Sam, "/update");
        var uniqueId = Guid.NewGuid();
        var updated = await pippin.V1.Drive.UpdateFile(new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Peer,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = file.ToFileIdentifier(),
            Recipients = [frodo.Identity, sam.Identity],
            Manifest = EmptyManifest
        }, Metadata(uniqueId, "updated"), []);
        Assert.That(updated.StatusCode, Is.EqualTo(HttpStatusCode.OK), updated.Error?.Content);

        await pippin.Sync.DrainOutboxAsync();
        foreach (var recipient in new[] { frodo, sam })
        {
            await recipient.Sync.ProcessInboxAsync(drive, int.MaxValue);
            var header = await recipient.V1.Drive.QueryByGlobalTransitId(file);
            Assert.That(header.Content!.SearchResults.Single().FileMetadata.AppData.Content, Is.EqualTo("updated"),
                $"{recipient.Identity}'s copy");
        }

        // and once both were sent, the transient file is gone
        await AssertNothingStoredAsync(pippin, TransientDrive, uniqueId);
    }

    /// <summary>A file staged on the transient drive with no recipients is the client's: kept, as one it overwrites later</summary>
    [Test]
    public async Task AFileStagedOnTheTransientDriveWithoutRecipientsIsKept()
    {
        var pippin = await LoginAsOwner(Identities.Pippin);
        var uniqueId = Guid.NewGuid();

        var staged = await pippin.Drives.Writer.CreateNewUnencryptedFile(TransientDrive, Metadata(uniqueId, "staged"), EmptyManifest, []);
        Assert.That(staged.StatusCode, Is.EqualTo(HttpStatusCode.OK), staged.Error?.Content);

        var stored = await pippin.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, TransientDrive);
        Assert.That(stored.StatusCode, Is.EqualTo(HttpStatusCode.OK), stored.Error?.Content);
    }

    //

    private static UploadFileMetadata Metadata(Guid uniqueId, string content)
    {
        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected, allowDistribution: true);
        metadata.AppData.UniqueId = uniqueId;
        metadata.AppData.Content = content;
        return metadata;
    }

    /// <summary>An app of <paramref name="owner"/>'s that writes <paramref name="drive"/> and reads peers, but may not send to them</summary>
    private static Task<AppSession> ReadOnlyAppAsync(OwnerSession owner, TargetDrive drive) =>
        AppSession.SetupAsync(owner, drive, DrivePermission.ReadWrite, [PermissionKeys.UseTransitRead]);

    /// <summary>Refused by the send's own check: a 403 from the drive's Write check would store nothing either</summary>
    private static void AssertRefusedForSending(IApiResponse response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), response.Error?.Content);
        Assert.That(response.Error?.Content, Does.Contain("requires UseTransitWrite"));
    }

    /// <summary>Answered 200, with Merry's send not queued: she isn't connected to Pippin</summary>
    private static void AssertQueuedForNoOne(IApiResponse response, Dictionary<string, TransferStatus>? recipientStatus)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(recipientStatus?[Merry.DomainName], Is.EqualTo(TransferStatus.EnqueuedFailed));
    }

    private static async Task AssertNothingStoredAsync(OwnerSession owner, Guid driveId, Guid uniqueId)
    {
        var stored = await owner.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, driveId);
        Assert.That(stored.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            $"the write left file {stored.Content?.FileId} on drive {driveId}");
    }

    /// <summary>Frodo's drive, which his circle lets Pippin write</summary>
    private async Task<(OwnerSession Pippin, OwnerSession Frodo, TargetDrive Drive)> ArrangeAsync()
    {
        var pippin = await LoginAsOwner(Identities.Pippin);
        var frodo = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await CollabChannelFlow.SetupAsync(frodo, drive, [pippin], driveName: "Frodo's drive");
        return (pippin, frodo, drive);
    }
}
