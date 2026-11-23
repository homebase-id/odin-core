#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
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
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// A send or a peer update from a caller without <c>UseTransitWrite</c> is refused before anything is stored (#2155).
/// A peer update or a transient upload is stored on the sender's transient drive until the outbox sends it; one stored
/// and then refused stayed there for good, and its uniqueId refused every later update of the same file.
///
/// Layout: Pippin sends; Frodo hosts the drive, and grants Pippin Write through a circle.
/// </summary>
[TestFixture]
public class RefusedPeerSendTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Pippin];

    private static readonly Guid TransientDrive = SystemDriveConstants.TransientTempDrive.Alias;

    private static UploadManifest EmptyManifest => new() { PayloadDescriptors = [] };

    [Test]
    public async Task ARefusedPeerUpdateStoresNothing_SoTheNextOneGoesThrough()
    {
        var (pippin, frodo, drive) = await ArrangeAsync();
        var gtid = await V2PeerUpdateByGtidTests.TransferAsync(pippin, frodo, drive, Metadata(Guid.NewGuid(), "first"), []);
        var uniqueId = Guid.NewGuid();
        var readOnlyApp = await ReadOnlyAppAsync(pippin, SystemDriveConstants.TransientTempDrive);

        var refused = await V2PeerUpdateByGtidTests.UpdateAsync(readOnlyApp, frodo.Identity, drive, gtid, Metadata(uniqueId, "refused"),
            V2PeerUpdateByGtidTests.Instructions(drive, EmptyManifest));
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), refused.Error?.Content);
        await AssertNothingStoredAsync(pippin, TransientDrive, uniqueId);

        var sent = await V2PeerUpdateByGtidTests.UpdateAsync(pippin, frodo.Identity, drive, gtid, Metadata(uniqueId, "sent"),
            V2PeerUpdateByGtidTests.Instructions(drive, EmptyManifest));
        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), sent.Error?.Content);

        await pippin.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive, int.MaxValue);
        var header = await frodo.V1.Drive.QueryByGlobalTransitId(new GlobalTransitIdFileIdentifier { GlobalTransitId = gtid, TargetDrive = drive });
        Assert.That(header.Content!.SearchResults.Single().FileMetadata.AppData.Content, Is.EqualTo("sent"));
    }

    [Test]
    public async Task ARefusedTransientUploadStoresNothing_SoTheNextOneGoesThrough()
    {
        var (pippin, frodo, drive) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        var transit = new TransitOptions { IsTransient = true, Recipients = [frodo.Identity.DomainName], RemoteTargetDrive = drive };
        var readOnlyApp = await ReadOnlyAppAsync(pippin, SystemDriveConstants.TransientTempDrive);

        var refused = await readOnlyApp.Drives.Writer.CreateNewUnencryptedFile(TransientDrive, Metadata(uniqueId, "refused"),
            EmptyManifest, [], transit);
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), refused.Error?.Content);
        await AssertNothingStoredAsync(pippin, TransientDrive, uniqueId);

        var sent = await pippin.Drives.Writer.CreateNewUnencryptedFile(TransientDrive, Metadata(uniqueId, "sent"), EmptyManifest, [], transit);
        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), sent.Error?.Content);
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

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), refused.Error?.Content);
        await AssertNothingStoredAsync(pippin, localDrive.Alias, uniqueId);
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

    private static async Task AssertNothingStoredAsync(OwnerSession owner, Guid driveId, Guid uniqueId)
    {
        var stored = await owner.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, driveId);
        Assert.That(stored.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            $"the refused write stored file {stored.Content?.FileId} on drive {driveId}");
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
