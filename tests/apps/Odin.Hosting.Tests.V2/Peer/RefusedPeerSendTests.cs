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

    [Test]
    public async Task ARefusedPeerUpdateStoresNothing_SoTheNextOneGoesThrough()
    {
        var (pippin, frodo, drive, gtid) = await FrodosFileFromPippinAsync();
        var uniqueId = Guid.NewGuid();
        var readOnlyApp = await AppSession.SetupAsync(pippin, SystemDriveConstants.TransientTempDrive, DrivePermission.ReadWrite,
            [PermissionKeys.UseTransitRead]);

        var refused = await V2PeerUpdateByGtidTests.UpdateAsync(readOnlyApp, frodo.Identity, drive, gtid, Metadata(uniqueId, "refused"),
            V2PeerUpdateByGtidTests.Instructions(drive, new UploadManifest { PayloadDescriptors = [] }));
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), refused.Error?.Content);
        await AssertNothingStoredAsync(pippin, TransientDrive, uniqueId);

        var sent = await V2PeerUpdateByGtidTests.UpdateAsync(pippin, frodo.Identity, drive, gtid, Metadata(uniqueId, "sent"),
            V2PeerUpdateByGtidTests.Instructions(drive, new UploadManifest { PayloadDescriptors = [] }));
        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), sent.Error?.Content);

        await pippin.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive, int.MaxValue);
        var header = await frodo.V1.Drive.QueryByGlobalTransitId(new GlobalTransitIdFileIdentifier { GlobalTransitId = gtid, TargetDrive = drive });
        Assert.That(header.Content!.SearchResults.Single().FileMetadata.AppData.Content, Is.EqualTo("sent"));
    }

    [Test]
    public async Task ARefusedTransientUploadStoresNothing_SoTheNextOneGoesThrough()
    {
        var (pippin, frodo, drive, _) = await FrodosFileFromPippinAsync();
        var uniqueId = Guid.NewGuid();
        var transit = new TransitOptions { IsTransient = true, Recipients = [frodo.Identity.DomainName], RemoteTargetDrive = drive };
        var readOnlyApp = await AppSession.SetupAsync(pippin, SystemDriveConstants.TransientTempDrive, DrivePermission.ReadWrite,
            [PermissionKeys.UseTransitRead]);

        var refused = await readOnlyApp.Drives.Writer.CreateNewUnencryptedFile(TransientDrive, Metadata(uniqueId, "refused"),
            new UploadManifest { PayloadDescriptors = [] }, [], transit);
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), refused.Error?.Content);
        await AssertNothingStoredAsync(pippin, TransientDrive, uniqueId);

        var sent = await pippin.Drives.Writer.CreateNewUnencryptedFile(TransientDrive, Metadata(uniqueId, "sent"),
            new UploadManifest { PayloadDescriptors = [] }, [], transit);
        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), sent.Error?.Content);
    }

    /// <summary>A local upload that names recipients is refused whole: before, the local file was stored, then the send refused</summary>
    [Test]
    public async Task ARefusedLocalUploadWithRecipientsStoresNothing()
    {
        var (pippin, frodo, drive, _) = await FrodosFileFromPippinAsync();
        var uniqueId = Guid.NewGuid();
        var localDrive = TargetDrive.NewTargetDrive();
        await pippin.Admin.CreateDrive(localDrive, "local drive");
        var readOnlyApp = await AppSession.SetupAsync(pippin, localDrive, DrivePermission.ReadWrite, [PermissionKeys.UseTransitRead]);

        var refused = await readOnlyApp.Drives.Writer.CreateNewUnencryptedFile(localDrive.Alias, Metadata(uniqueId, "refused"),
            new UploadManifest { PayloadDescriptors = [] }, [],
            new TransitOptions { Recipients = [frodo.Identity.DomainName], RemoteTargetDrive = drive });

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

    private static async Task AssertNothingStoredAsync(OwnerSession owner, Guid driveId, Guid uniqueId)
    {
        var stored = await owner.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, driveId);
        Assert.That(stored.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            $"the refused write stored file {stored.Content?.FileId} on drive {driveId}");
    }

    /// <summary>A file Pippin sent to Frodo's drive, which Frodo's circle lets him write</summary>
    private async Task<(OwnerSession Pippin, OwnerSession Frodo, TargetDrive Drive, Guid Gtid)> FrodosFileFromPippinAsync()
    {
        var pippin = await LoginAsOwner(Identities.Pippin);
        var frodo = await LoginAsOwner(Identities.Frodo);

        var drive = TargetDrive.NewTargetDrive();
        await CollabChannelFlow.SetupAsync(frodo, drive, [pippin], driveName: "Frodo's drive");

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected, allowDistribution: true);
        var response = await pippin.V1.PeerDirect.TransferNewFile(drive, metadata, [frodo.Identity], null,
            new UploadManifest { PayloadDescriptors = [] }, []);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);

        await pippin.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive, int.MaxValue);
        return (pippin, frodo, drive, response.Content!.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId);
    }
}
