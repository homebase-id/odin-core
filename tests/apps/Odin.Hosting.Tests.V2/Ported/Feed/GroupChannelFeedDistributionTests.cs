using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Util;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.Feed.GroupChannel;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/_Universal/Feed/GroupChannel/GroupChannelFeedDistributionTests.cs
///
/// Tests that guests (via youauth) can post to a group channel and those posts
/// are distributed to all connected identities which follow the channel.
/// </summary>
/// <remarks>
/// Checked port. <b>Entirely <c>[Ignore]("need to fix guest access bug")</c>, carried as-is</b> —
/// this moved without ever having run.
/// <list type="bullet">
/// <item>No caller matrix in the original and none here.</item>
/// <item>The guest who posts needs <c>PermissionKeys.SendOnBehalfOfOwner</c>, which
/// <see cref="CallerSpec.Guest"/> cannot express — it grants a drive permission and no permission
/// keys. The guest is built from
/// <see cref="GuestSession.SetupAsync(OwnerSession, Odin.Services.Base.PermissionSetGrantRequest, AsciiDomainName?)"/>
/// instead, which takes the whole grant and the domain — keeping the original's use of the author's
/// own identity as the domain name rather than a generated one. The test does not run, so none of
/// this has been exercised.</item>
/// <item>The original's only distribution step was a commented-out <c>WaitForEmptyOutbox</c>, with
/// "Feed distribution should happen here" beside it — so as written the test asserts on a feed that
/// nothing was ever asked to deliver to. Carried verbatim, commented-out line included; whoever
/// un-ignores this will need <c>Sync.DrainOutboxAsync()</c> on the group identity followed by
/// <c>Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive)</c> on Sam.</item>
/// <item>Trailing disconnect / unfollow calls were cleanup only and are dropped — per-test reset
/// covers them.</item>
/// </list>
/// </remarks>
[TestFixture]
public class GroupChannelFeedDistributionTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Pippin];

    [Test]
    [Ignore("need to fix guest access bug")]
    public async Task FeedDistributionSucceedsWhenGuestPostsToGroupChannel()
    {
        // Group channel is hosted by frodo
        // Create a channel drive with attribute IsGroupChannel=true
        // Sam and pippin are guests that follow the channel
        // Pippin posts content
        // the content is in both Sams and Frodo's feed drives

        var groupOwnerClient = await LoginAsOwner(Identities.Frodo);
        var samOwnerClient = await LoginAsOwner(Identities.Sam);
        var pippinOwnerClient = await LoginAsOwner(Identities.Pippin);

        var (channelDrive, groupCircleId) = await CreateGroupChannelAsync(groupOwnerClient);

        await SetupConnectionAsync(groupOwnerClient, samOwnerClient, groupCircleId);
        await SetupConnectionAsync(groupOwnerClient, pippinOwnerClient, groupCircleId);

        var (uploadResult, encryptedContent64) = await PostContentAsync(
            author: pippinOwnerClient,
            groupOwnerClient: groupOwnerClient,
            targetDrive: channelDrive);

        //
        // Feed distribution should happen here
        //
        // await groupOwnerClient.Sync.DrainOutboxAsync();

        //
        // Validation - check that sam has the file in his feed
        //
        var queryFeedResponse = await samOwnerClient.V1.Drive.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = WellKnownAppDrives.FeedDrive,
                GlobalTransitId = [uploadResult.GlobalTransitId.GetValueOrDefault()]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        });

        Assert.That(queryFeedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var thePost = queryFeedResponse.Content?.SearchResults.Single();
        Assert.That(thePost, Is.Not.Null);
        Assert.That(thePost.FileMetadata.IsEncrypted, Is.True);
        Assert.That(thePost.FileMetadata.AppData.Content, Is.EqualTo(encryptedContent64));
    }

    private static async Task SetupConnectionAsync(
        OwnerSession groupOwnerClient, OwnerSession identityOwnerClient, Guid groupCircleId)
    {
        var sendConnectionResponse = await identityOwnerClient.Connections.SendConnectionRequest(groupOwnerClient.Identity);
        Assert.That(sendConnectionResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var acceptConnectionResponse =
            await groupOwnerClient.Connections.AcceptConnectionRequest(identityOwnerClient.Identity, [groupCircleId]);
        Assert.That(acceptConnectionResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var followResponse = await identityOwnerClient.V1.Follower.FollowIdentity(groupOwnerClient.Identity,
            FollowerNotificationType.AllNotifications);
        Assert.That(followResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    private static async Task<(TargetDrive channelDrive, Guid groupCircleId)> CreateGroupChannelAsync(
        OwnerSession groupOwnerClient)
    {
        var channelDrive = new TargetDrive
        {
            Alias = Guid.Parse("77777777-a226-4baa-b650-63cdb1cda924"),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await groupOwnerClient.Admin.CreateDrive(channelDrive, "a group channel", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        var groupCircleId = Guid.Parse("55555555-a226-4baa-b650-63cdb1cda924");
        var grant = new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = channelDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            }
        };

        await groupOwnerClient.Admin.CreateCircle(groupCircleId, "a group circle", grant);

        return (channelDrive, groupCircleId);
    }

    private static async Task<(UploadResult uploadResult, string encryptedJsonContent64)> PostContentAsync(
        OwnerSession author, OwnerSession groupOwnerClient, TargetDrive targetDrive)
    {
        //
        // first create an API client to use the guest api
        //
        var driveGrants = new List<DriveGrantRequest>
        {
            new()
            {
                PermissionedDrive = new PermissionedDrive
                {
                    Drive = targetDrive,
                    Permission = DrivePermission.ReadWrite
                }
            }
        };

        // CallerSpec.Guest grants a drive permission and no permission keys, and names the domain
        // itself; the original needs SendOnBehalfOfOwner and the author's own identity as the domain,
        // which is what GuestSession's grant + domain overload takes.
        var guest = await GuestSession.SetupAsync(groupOwnerClient, new PermissionSetGrantRequest
        {
            Drives = driveGrants,
            PermissionSet = new PermissionSet(PermissionKeys.SendOnBehalfOfOwner)
        }, new AsciiDomainName(author.Identity.DomainName));

        var guestDriveClient = guest.V1.Drive;

        //
        // Post a file as guest
        //
        const int fileType = 1039;
        const string content = "some secured friends only content";
        var file = SampleMetadataData.CreateWithContent(fileType, content, AccessControlList.Connected);
        file.AllowDistribution = true;

        var uploadResponse = await guestDriveClient.UploadNewEncryptedMetadata(
            targetDrive,
            file);

        Assert.That(uploadResponse.response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        return (uploadResponse.response.Content, uploadResponse.encryptedJsonContent64);
    }
}
