#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Notifications;

/// <summary>
/// A comment someone else leaves on a post in one of the owner's channels notifies the owner as the Feed app's
/// comment notification.  The check compared the referenced drive's alias with the channel drive type, so it never
/// matched a real channel and the notification was never sent (#2205).
/// </summary>
[TestFixture]
public class FeedCommentNotificationTests : V2Fixture
{
    // FeedNotificationMapper.CommentNotificationTypeId
    private static readonly Guid CommentNotificationTypeId = Guid.Parse("1e08b70a-3826-4840-8372-18410bfc02c7");

    [Test]
    public async Task AGuestsCommentOnAChannelPostNotifiesTheOwner()
    {
        var channelDrive = new TargetDrive { Alias = Guid.NewGuid(), Type = SystemDriveConstants.ChannelDriveType };
        var spec = CallerSpec.Guest(new DriveSpec(channelDrive), DrivePermission.Read | DrivePermission.Comment);
        var (guest, owner) = await SetupCallerWithOwner(spec);

        var postMetadata = SampleMetadataData.CreateWithContent(fileType: default, "a post", AccessControlList.Anonymous);
        postMetadata.AllowDistribution = true;
        var post = await owner.V1.Drive.UploadNewMetadata(channelDrive, postMetadata);
        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.OK), post.Error?.Content);

        var commentMetadata = SampleMetadataData.CreateWithContent(fileType: default, "a comment", AccessControlList.Anonymous);
        commentMetadata.ReferencedFile = post.Content!.GlobalTransitIdFileIdentifier;
        var comment = await guest.V1.Drive.UploadNewMetadata(channelDrive, commentMetadata, FileSystemType.Comment);
        Assert.That(comment.StatusCode, Is.EqualTo(HttpStatusCode.OK), comment.Error?.Content);

        var list = await owner.V1.Notifications.GetList(100);
        Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var notification = list.Content!.Results.SingleOrDefault(n =>
            n.Options.AppId == SystemAppConstants.FeedAppId && n.Options.TypeId == CommentNotificationTypeId);
        Assert.That(notification, Is.Not.Null, "the owner is notified of the comment");
        Assert.That(notification!.Options.TagId, Is.EqualTo(post.Content.GlobalTransitId), "tagged with the post");
    }
}
