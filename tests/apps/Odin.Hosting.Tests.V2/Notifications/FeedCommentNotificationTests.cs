#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.Tests.V2.Ported.Reactions;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.DataSubscription;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Notifications;

/// <summary>
/// A comment someone else leaves on a post in one of the owner's channels notifies the owner as the Feed app's
/// comment notification.  The check compared the referenced drive's alias with the channel drive type, so it never
/// matched a real channel and the notification was never sent (#2205).
/// </summary>
[TestFixture]
public class FeedCommentNotificationTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private static readonly Guid CommentNotificationTypeId = FeedNotificationMapper.CommentNotificationTypeId;

    [Test]
    public async Task AGuestsCommentOnAChannelPostNotifiesTheOwner()
    {
        var channelDrive = new TargetDrive { Alias = Guid.NewGuid(), Type = SystemDriveConstants.ChannelDriveType };
        var spec = CallerSpec.Guest(new DriveSpec(channelDrive), DrivePermission.Read | DrivePermission.Comment);
        var (guest, owner) = await SetupCallerWithOwner(spec);

        var post = await ChannelPosts.UploadPostAsync(owner, channelDrive, "a post", acl: AccessControlList.Anonymous);

        var commentMetadata = SampleMetadataData.CreateWithContent(fileType: default, "a comment", AccessControlList.Anonymous);
        commentMetadata.ReferencedFile = post.GlobalTransitIdFileIdentifier;
        var comment = await guest.V1.Drive.UploadNewMetadata(channelDrive, commentMetadata, FileSystemType.Comment);
        Assert.That(comment.StatusCode, Is.EqualTo(HttpStatusCode.OK), comment.Error?.Content);

        var list = await owner.V1.Notifications.GetList(100);
        Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var notification = list.Content!.Results.SingleOrDefault(n =>
            n.Options.AppId == SystemAppConstants.FeedAppId && n.Options.TypeId == CommentNotificationTypeId);
        Assert.That(notification, Is.Not.Null, "the owner is notified of the comment");
        Assert.That(notification!.Options.TagId, Is.EqualTo(post.GlobalTransitId), "tagged with the post");
    }

    /// <summary>
    /// A connected identity's comment, sent over peer transfer, is notified once, by its write: the transfer itself
    /// raises no notification for a comment (it did, and the write then notified a second time)
    /// </summary>
    [Test]
    public async Task APeersCommentOnAChannelPostNotifiesTheOwnerOnce()
    {
        var commenter = await LoginAsOwner(Identities.Frodo);
        var owner = await LoginAsOwner(Identities.Sam);
        var channelDrive = await PeerFlow.CreatePeerDriveAsync(commenter, owner,
            DrivePermission.Read | DrivePermission.WriteReactionsAndComments,
            drive: new TargetDrive { Alias = Guid.NewGuid(), Type = SystemDriveConstants.ChannelDriveType });

        var post = await ChannelPosts.UploadPostAsync(owner, channelDrive, "a post", acl: AccessControlList.Connected);

        var commentMetadata = SampleMetadataData.CreateWithContent(fileType: default, "a comment", AccessControlList.Connected);
        commentMetadata.AllowDistribution = true;
        commentMetadata.ReferencedFile = post.GlobalTransitIdFileIdentifier;
        var comment = await commenter.V1.Drive.UploadNewMetadata(channelDrive, commentMetadata,
            new TransitOptions { Recipients = new List<string> { owner.Identity } }, FileSystemType.Comment);
        Assert.That(comment.StatusCode, Is.EqualTo(HttpStatusCode.OK), comment.Error?.Content);
        await commenter.Sync.DrainOutboxAsync();

        var list = await owner.V1.Notifications.GetList(100);
        Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var notifications = list.Content!.Results.Where(n =>
            n.Options.AppId == SystemAppConstants.FeedAppId && n.Options.TypeId == CommentNotificationTypeId).ToList();
        Assert.That(notifications, Has.Count.EqualTo(1), "the owner is notified of the comment once");
    }

    [Test]
    public async Task TheOwnersOwnCommentDoesNotNotify()
    {
        var channelDrive = new TargetDrive { Alias = Guid.NewGuid(), Type = SystemDriveConstants.ChannelDriveType };
        var (_, owner) = await SetupCallerWithOwner(CallerSpec.Owner(new DriveSpec(channelDrive)));
        var post = await ChannelPosts.UploadPostAsync(owner, channelDrive, "a post", acl: AccessControlList.Anonymous);

        await ChannelPosts.UploadCommentAsync(owner, channelDrive, post.GlobalTransitIdFileIdentifier, "my own comment");

        Assert.That(await CommentNotificationsAsync(owner), Is.Empty);
    }

    [Test]
    public async Task AGuestsCommentOnAPostThatIsNotInAChannelDoesNotNotify()
    {
        var spec = CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read | DrivePermission.Comment);
        var (guest, owner) = await SetupCallerWithOwner(spec);
        var post = await ChannelPosts.UploadPostAsync(owner, spec.TargetDrive, "a post", acl: AccessControlList.Anonymous);

        var commentMetadata = SampleMetadataData.CreateWithContent(fileType: default, "a comment", AccessControlList.Anonymous);
        commentMetadata.ReferencedFile = post.GlobalTransitIdFileIdentifier;
        var comment = await guest.V1.Drive.UploadNewMetadata(spec.TargetDrive, commentMetadata, FileSystemType.Comment);
        Assert.That(comment.StatusCode, Is.EqualTo(HttpStatusCode.OK), comment.Error?.Content);

        Assert.That(await CommentNotificationsAsync(owner), Is.Empty);
    }

    private static async Task<List<Odin.Services.AppNotifications.Data.AppNotification>> CommentNotificationsAsync(OwnerSession owner)
    {
        var list = await owner.V1.Notifications.GetList(100);
        Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return list.Content!.Results.Where(n =>
            n.Options.AppId == SystemAppConstants.FeedAppId && n.Options.TypeId == CommentNotificationTypeId).ToList();
    }
}
