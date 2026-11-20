using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Base;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Ported.Concepts;
using Odin.Hosting.Tests.V2.Ported.DataSubscription;
using Odin.Hosting.Tests.V2.Ported.Transit;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Security;

/// <summary>
/// Legitimate feed updates must keep working now that only an item's stored sender may update it.
/// Every request here comes from a stock server.
/// </summary>
[TestFixture]
public class FeedUpdateRegressionTests : V2Fixture
{
    private static readonly TargetDrive ChannelDrive = new()
    {
        Alias = Guid.Parse("c0111ab0-c8a7-4001-8000-000000000042"),
        Type = SystemDriveConstants.ChannelDriveType
    };

    protected override string[] HostIdentities => [Identities.Collab, Identities.Frodo, Identities.Sam, Identities.Merry];

    [Test]
    public async Task CollaborativeHost_UpdatesItsOwnEncryptedItem_OnAFollowersFeed()
    {
        var (collab, member, follower) = await ArrangeCollabChannelAsync();
        var keyHeader = KeyHeader.NewRandom16();

        var (response, metadata, _) = await CollabScenario.PostNewEncryptedFileOverPeerDirectAsync(
            member, ChannelDrive, collab, keyHeader, AccessControlList.Connected);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await RedistributeAsync(collab);

        var remote = response.Content!.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();
        var before = await SingleFeedItemAsync(follower, remote.GlobalTransitId.GetValueOrDefault());
        Assert.That(before.FileMetadata.SenderOdinId, Is.EqualTo(collab.Identity.DomainName));

        metadata.AppData.Content = "edited content";
        metadata.AppData.DataType = 5678;
        metadata.VersionTag = await VersionTagOnChannelAsync(collab, remote);
        keyHeader.Iv = ByteArrayUtil.GetRndByteArray(16);

        var (update, _, _, _) = await member.V1.Drive.UpdateEncryptedFile(
            MetadataOnlyPeerUpdate(remote, collab.Identity), metadata, [], keyHeader);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await member.Sync.DrainOutboxAsync();
        await RedistributeAsync(collab);

        var after = await SingleFeedItemAsync(follower, remote.GlobalTransitId.GetValueOrDefault());
        Assert.That(after.FileMetadata.AppData.DataType, Is.EqualTo(5678), "the edit should reach the feed");
        Assert.That(after.FileMetadata.SenderOdinId, Is.EqualTo(collab.Identity.DomainName));
    }

    [Test]
    public async Task OriginalSender_UpdatesItsOwnEncryptedItem_OnANormalChannel()
    {
        const int fileType = 20872;
        var sam = await LoginAsOwner(Identities.Sam);
        var frodo = await LoginAsOwner(Identities.Frodo);

        await sam.Admin.CreateDrive(ChannelDrive, "A channel", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);
        var readers = Guid.NewGuid();
        await sam.Admin.CreateCircle(readers, "Channel readers",
            TestUtils.CreatePermissionGrantRequest(ChannelDrive, DrivePermission.Read));
        await DataSubscriptionScenario.ConnectAsync(sam, frodo, readers);
        await DataSubscriptionScenario.FollowAsync(frodo, sam);

        var keyHeader = KeyHeader.NewRandom16();
        var metadata = SampleMetadataData.Create(fileType: fileType);
        metadata.AllowDistribution = true;
        metadata.AccessControlList = AccessControlList.Connected;
        metadata.AppData.Content = "first version";

        var (upload, _) = await sam.V1.Drive.UploadNewEncryptedMetadata(ChannelDrive, metadata, keyHeader: keyHeader);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await sam.Sync.DrainOutboxAsync();

        var gtid = upload.Content!.GlobalTransitId.GetValueOrDefault();
        var before = await SingleFeedItemAsync(frodo, gtid);
        Assert.That(before.FileMetadata.SenderOdinId, Is.EqualTo(sam.Identity.DomainName));

        metadata.AppData.Content = "second version";
        metadata.VersionTag = upload.Content.NewVersionTag;
        var (update, updatedContent64) = await sam.V1.Drive.UpdateExistingEncryptedMetadata(upload.Content.File, keyHeader, metadata);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await sam.Sync.DrainOutboxAsync();

        var after = await SingleFeedItemAsync(frodo, gtid);
        Assert.That(after.FileMetadata.AppData.Content, Is.EqualTo(updatedContent64), "the edit should reach the feed");
        Assert.That(after.FileMetadata.SenderOdinId, Is.EqualTo(sam.Identity.DomainName));
    }

    [Test]
    public async Task CollaborativeHost_UpdatesItsOwnUnencryptedItem_OnAFollowersFeed()
    {
        var (collab, member, follower) = await ArrangeCollabChannelAsync();

        var metadata = SampleMetadataData.Create(fileType: 20873);
        metadata.AllowDistribution = true;
        metadata.AccessControlList = AccessControlList.Connected;
        metadata.AppData.Content = "first version";

        var response = await member.V1.PeerDirect.TransferNewFile(ChannelDrive, metadata, [collab.Identity], null,
            new UploadManifest { PayloadDescriptors = [] }, []);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await member.Sync.DrainOutboxAsync();
        await RedistributeAsync(collab);

        var remote = response.Content!.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();
        var before = await SingleFeedItemAsync(follower, remote.GlobalTransitId.GetValueOrDefault());
        Assert.That(before.FileMetadata.SenderOdinId, Is.EqualTo(collab.Identity.DomainName));

        metadata.AppData.Content = "second version";
        metadata.VersionTag = await VersionTagOnChannelAsync(collab, remote);
        var update = await member.V1.Drive.UpdateFile(MetadataOnlyPeerUpdate(remote, collab.Identity), metadata, []);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await member.Sync.DrainOutboxAsync();
        await RedistributeAsync(collab);

        var after = await SingleFeedItemAsync(follower, remote.GlobalTransitId.GetValueOrDefault());
        Assert.That(after.FileMetadata.AppData.Content, Is.EqualTo("second version"), "the edit should reach the feed");
        Assert.That(after.FileMetadata.SenderOdinId, Is.EqualTo(collab.Identity.DomainName));
    }

    /// <summary>
    /// A member posts to their own channel and to a collaborative host, so one global transit id reaches
    /// the follower from two senders. The first sender keeps the item; the host's update is refused.
    /// </summary>
    [Test]
    public async Task SameItemFromTwoHosts_UpdateFromTheSecondHost_IsRefused()
    {
        var (collab, member, follower) = await ArrangeCollabChannelAsync();

        await member.Admin.CreateDrive(ChannelDrive, "The member's own channel", allowAnonymousReads: true,
            ownerOnly: false, allowSubscriptions: true);
        await DataSubscriptionScenario.FollowAsync(follower, member);

        var metadata = SampleMetadataData.Create(fileType: 20874);
        metadata.AllowDistribution = true;
        metadata.AccessControlList = AccessControlList.Anonymous;
        metadata.AppData.Content = "first version";

        var upload = await member.V1.Drive.UploadNewMetadata(ChannelDrive, metadata,
            new TransitOptions { Recipients = [collab.Identity.DomainName], RemoteTargetDrive = ChannelDrive });
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await member.Sync.DrainOutboxAsync();

        var gtid = upload.Content!.GlobalTransitId.GetValueOrDefault();
        var first = await SingleFeedItemAsync(follower, gtid);
        Assert.That(first.FileMetadata.SenderOdinId, Is.EqualTo(member.Identity.DomainName),
            "arrange: the member's own server should deliver first");

        await RedistributeAsync(collab);

        var remote = new FileIdentifier { GlobalTransitId = gtid, TargetDrive = ChannelDrive };
        metadata.AppData.Content = "edited through the host";
        metadata.VersionTag = await VersionTagOnChannelAsync(collab, remote);
        var update = await member.V1.Drive.UpdateFile(MetadataOnlyPeerUpdate(remote, collab.Identity), metadata, []);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await member.Sync.DrainOutboxAsync();
        await RedistributeAsync(collab);

        var after = await SingleFeedItemAsync(follower, gtid);
        Assert.That(after.FileMetadata.AppData.Content, Is.EqualTo("first version"),
            "only the identity that delivered the item first may update it");
        Assert.That(after.FileMetadata.SenderOdinId, Is.EqualTo(member.Identity.DomainName));
    }

    private async Task<(OwnerSession Collab, OwnerSession Member, OwnerSession Follower)> ArrangeCollabChannelAsync()
    {
        var collab = await LoginAsOwner(Identities.Collab);
        var member = await LoginAsOwner(Identities.Merry);
        var follower = await LoginAsOwner(Identities.Frodo);

        // The follower is a member too: the host only redistributes to followers holding keyed Read.
        await CollabScenario.PrepareScenarioAsync(collab, [member, follower], ChannelDrive,
            DrivePermission.ReadWrite, "A collaborative channel", allowAnonymousReads: true);
        await DataSubscriptionScenario.FollowAsync(follower, collab);

        return (collab, member, follower);
    }

    private static async Task RedistributeAsync(OwnerSession collab)
    {
        await collab.Sync.ProcessInboxAsync(ChannelDrive);
        await collab.Sync.DrainOutboxAsync();
    }

    private static async Task<Guid?> VersionTagOnChannelAsync(OwnerSession collab, FileIdentifier remote)
    {
        var header = await TransitScenario.SingleByGlobalTransitIdAsync(collab, remote.ToGlobalTransitIdFileIdentifier());
        return header.FileMetadata.VersionTag;
    }

    private static async Task<SharedSecretEncryptedFileHeader> SingleFeedItemAsync(OwnerSession owner, Guid gtid)
    {
        await owner.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        return await TransitScenario.SingleByGlobalTransitIdAsync(owner, new GlobalTransitIdFileIdentifier
        {
            GlobalTransitId = gtid,
            TargetDrive = WellKnownAppDrives.FeedDrive
        });
    }

    private static FileUpdateInstructionSet MetadataOnlyPeerUpdate(FileIdentifier remote, OdinId recipient) => new()
    {
        Locale = UpdateLocale.Peer,
        TransferIv = ByteArrayUtil.GetRndByteArray(16),
        File = remote,
        Recipients = new List<OdinId> { recipient },
        Manifest = new UploadManifest { PayloadDescriptors = [] }
    };
}
