using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Hosting.Tests.V2.Ported.Feed;

/// <summary>
/// The arrange every feed fixture shares — a secured channel drive behind a 'Friends Only' circle plus
/// a public channel drive, with one post on each — and the feed-drive query they all read back with.
/// Peer of <c>Ported/DriveQuery/QueryScenario.cs</c>.
/// </summary>
/// <remarks>
/// Each V1 original carried its own <c>Prepare…IdentityWithChannelsAndPosts</c>; the four ports had
/// four copies of it, line-for-line identical but for the public post's ACL, three extra public
/// uploads in one, and the friends-only half alone in another. Those are the parameters here, so the
/// differences are visible at the call sites rather than buried in near-twin bodies.
/// </remarks>
internal static class FeedScenario
{
    public const string DefaultFriendsOnlyContent = "some secured friends only content";
    public const string DefaultPublicContent = "some public content";

    /// <summary>What <see cref="PrepareIdentityWithChannelsAndPostsAsync"/> put on the two channels.</summary>
    public sealed record PreparedFiles(string EncryptedFriendsFileContent64, string PublicFileContent);

    /// <summary>A file-type-filtered query over one drive, asking for the metadata header.</summary>
    public static QueryBatchRequest DriveQuery(TargetDrive targetDrive, List<int> fileTypes, int maxRecords = 10) => new()
    {
        QueryParams = new FileQueryParamsV1
        {
            TargetDrive = targetDrive,
            FileType = fileTypes
        },
        ResultOptionsRequest = new QueryBatchResultOptionsRequest
        {
            MaxRecords = maxRecords,
            IncludeMetadataHeader = true
        }
    };

    /// <summary><see cref="DriveQuery"/> over the feed drive. An empty <paramref name="fileTypes"/> filters on nothing.</summary>
    public static QueryBatchRequest FeedQuery(List<int> fileTypes, int maxRecords = 10) =>
        DriveQuery(WellKnownAppDrives.FeedDrive, fileTypes, maxRecords);

    /// <inheritdoc cref="FeedQuery(List{int}, int)"/>
    public static QueryBatchRequest FeedQuery(int fileType, int maxRecords = 10) =>
        FeedQuery([fileType], maxRecords);

    /// <summary>
    /// The identity creates the circle 'friends' with read access to a secured channel drive, posts one
    /// encrypted item to it, and posts one item — with <paramref name="publicPostAcl"/> — to a public
    /// channel drive.
    /// </summary>
    /// <param name="extraPublicPosts">
    /// Further public posts, each with a fresh random content string, uploaded but not asserted on.
    /// The public-followers fixture counts four files in the feed and gets them this way.
    /// </param>
    public static async Task<PreparedFiles> PrepareIdentityWithChannelsAndPostsAsync(
        OwnerSession owner,
        Guid circleId,
        int postFileType,
        AccessControlList publicPostAcl,
        string friendsOnlyContent = DefaultFriendsOnlyContent,
        string publicContent = DefaultPublicContent,
        int extraPublicPosts = 0)
    {
        var publicTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await owner.Admin.CreateDrive(publicTargetDrive, "Public Channel Drive", allowAnonymousReads: true,
            ownerOnly: false, allowSubscriptions: true);

        var encryptedFriendsFileContent64 =
            await PrepareFriendsOnlyChannelAsync(owner, circleId, postFileType, friendsOnlyContent);

        //
        // upload one post to public target drive
        //
        var publicFile = SampleMetadataData.CreateWithContent(postFileType, publicContent, publicPostAcl);
        publicFile.AllowDistribution = true;
        var publicFileUploadResult = await owner.V1.Drive.UploadNewMetadata(publicTargetDrive, publicFile);

        for (var i = 0; i < extraPublicPosts; i++)
        {
            publicFile.AppData.Content = Guid.NewGuid().ToString();
            await owner.V1.Drive.UploadNewMetadata(publicTargetDrive, publicFile);
        }

        // Carried from the originals: the first upload is asserted only after the extra ones have run,
        // and those extra ones are not asserted at all.
        Assert.That(publicFileUploadResult.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        return new PreparedFiles(encryptedFriendsFileContent64, publicContent);
    }

    /// <summary>
    /// The friends-only half alone: a secured channel drive, the 'Friends Only' circle granting Read on
    /// it, and one encrypted post. Returns the encrypted content as the server stored it.
    /// </summary>
    public static async Task<string> PrepareFriendsOnlyChannelAsync(
        OwnerSession owner,
        Guid circleId,
        int postFileType,
        string friendsOnlyContent = DefaultFriendsOnlyContent)
    {
        var friendsOnlyTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await owner.Admin.CreateDrive(friendsOnlyTargetDrive, "Secured Channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        await owner.Admin.CreateCircle(circleId, "Friends Only", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new()
                    {
                        Drive = friendsOnlyTargetDrive,
                        Permission = DrivePermission.Read
                    },
                }
            },
            PermissionSet = default
        });

        //
        // upload one post to friends target drive
        //
        var friendsFile = SampleMetadataData.CreateWithContent(postFileType, friendsOnlyContent, AccessControlList.Connected);
        friendsFile.AllowDistribution = true;
        var (response, encryptedJsonContent64) = await owner.V1.Drive.UploadNewEncryptedMetadata(
            friendsOnlyTargetDrive,
            friendsFile);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        return encryptedJsonContent64;
    }

    /// <summary>Both posts — the anonymous one and the secured one — are in <paramref name="follower"/>'s feed.</summary>
    public static async Task AssertHasAllExpectedFeedFilesAsync(
        OwnerSession follower,
        QueryBatchRequest feedQuery,
        PreparedFiles preparedFiles)
    {
        var queryFeedResponse = await follower.V1.Drive.QueryBatch(feedQuery);
        Assert.That(queryFeedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var feedSearchResults = queryFeedResponse.Content?.SearchResults?.ToList();
        Assert.That(feedSearchResults, Is.Not.Null);
        Assert.That(feedSearchResults.Count, Is.EqualTo(2));

        Assert.That(feedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted &&
            s.FileMetadata.AppData.Content == preparedFiles.EncryptedFriendsFileContent64));

        Assert.That(feedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted == false &&
            s.FileMetadata.AppData.Content == preparedFiles.PublicFileContent));
    }
}
