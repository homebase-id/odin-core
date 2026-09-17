using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive.Reactions;

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/AppAPI/Transit/Reactions/AppTransitReactionSenderPublicFiles.cs
///
/// What an app on one identity may do to a file on another identity's <em>anonymous</em> drive over
/// transit: react (gated by the tenant's
/// <see cref="TenantConfigFlagNames.AuthenticatedIdentitiesCanReactOnAnonymousDrives"/> flag) and, as
/// a connected identity, send a comment.
/// </summary>
/// <remarks>
/// Port notes:
/// <list type="bullet">
///   <item><description>
///     <c>CreateAppAndClient</c> is <see cref="AppTransitClients.CreateAppAsync"/>; the app's
///     <c>TransitReactionSender</c> and <c>TransitFileSender</c> clients are
///     <see cref="AppTransitClients.ReactionsFor"/> and
///     <see cref="AppTransitClients.TransferFileAsync"/>.
///   </description></item>
///   <item><description>
///     The add-reaction endpoint answers <b>204 NoContent</b>, not 200. The original asserted
///     <c>IsSuccessStatusCode</c>, which covered both; the exact code is asserted here.
///   </description></item>
///   <item><description>
///     <c>WaitForEmptyOutbox(TransientTempDrive)</c> is a passive poll of the outbox background
///     service, which the fast host never starts. It becomes
///     <see cref="PeerFlow.DistributeAsync(OwnerSession, OwnerSession, TargetDrive)"/>: drain Merry's
///     outbox, then process Pippin's inbox. The inbox half is new — the V1 framework's background
///     inbox processor did it invisibly — and without it the comment never reaches Pippin's index,
///     so it is a faithful translation of the original's intent rather than an added step.
///   </description></item>
///   <item><description>
///     The trailing <c>DisconnectFrom</c> asserted nothing and is dropped; per-test reset covers it.
///   </description></item>
/// </list>
/// Carried defect, behaviour left as found: the <c>[Ignore]</c>d
/// <see cref="AppCan_AddCommentOn_AnonymousDrive_With_CommentPermission"/> reads the comment back by
/// asking for <em>the original post's</em> file id under the Comment file system, not the comment's.
/// It is carried verbatim, <c>[Ignore]</c> and all, so it never runs either way.
/// </remarks>
[TestFixture]
public class AppTransitReactionSenderPublicFiles : V2Fixture
{

    protected override string[] HostIdentities => [Identities.Merry, Identities.Pippin];

    [Test]
    public async Task AppCan_SendAndGet_Public_ReactionContent()
    {
        // Prep
        var pippin = await LoginAsOwner(Identities.Pippin);
        var merry = await LoginAsOwner(Identities.Merry);
        var merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitRead);

        var remoteDrive = TargetDrive.NewTargetDrive();
        await pippin.Admin.CreateDrive(remoteDrive, "Some target drive", allowAnonymousReads: true);

        // Pippin uploads file
        var targetFile = await UploadStandardRandomPublicFileHeader(pippin, remoteDrive);

        const string reactionContent = ":k:";

        var request = new PeerAddReactionRequest
        {
            OdinId = pippin.Identity,
            Request = new AddRemoteReactionRequest
            {
                File = targetFile.uploadResult.GlobalTransitIdFileIdentifier,
                Reaction = reactionContent
            }
        };

        //
        // Send the reaction - TODO: this fails because there's no default access to WriteReactionsAndComments for anonymous drives
        //
        var addReactionResponse = await AppTransitClients.ReactionsFor(merryApp).AddReaction(request);
        Assert.That(addReactionResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        //
        // Validate reaction exists
        //
        var getReactionsResponse = await AppTransitClients.ReactionsFor(merryApp).GetAllReactions(new PeerGetReactionsRequest
        {
            OdinId = pippin.Identity,
            Request = new GetRemoteReactionsRequest
            {
                File = targetFile.uploadResult.GlobalTransitIdFileIdentifier,
                Cursor = default,
                MaxRecords = 100,
            }
        });

        Assert.That(getReactionsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getReactionsResponse.Content, Is.Not.Null);
        var theReaction = getReactionsResponse.Content!.Reactions.SingleOrDefault(sr =>
            sr.GlobalTransitIdFileIdentifier == targetFile.uploadResult.GlobalTransitIdFileIdentifier);

        Assert.That(theReaction, Is.Not.Null);
        Assert.That(theReaction!.ReactionContent, Is.EqualTo(reactionContent));
    }

    [Test]
    public async Task AppFails_SendReactionContent_ToAnonymousDriveWithout_ReactPermission()
    {
        // Prep
        var pippin = await LoginAsOwner(Identities.Pippin);
        var merry = await LoginAsOwner(Identities.Merry);
        var merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitRead);

        var remoteDrive = TargetDrive.NewTargetDrive();
        await pippin.Admin.CreateDrive(remoteDrive, "Some target drive", allowAnonymousReads: true);

        // Pippin uploads file
        var targetFile = await UploadStandardRandomPublicFileHeader(pippin, remoteDrive);

        //
        // Turn off the flag that allows authenticated identities to react
        //
        await pippin.Admin.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.AuthenticatedIdentitiesCanReactOnAnonymousDrives, false.ToString());

        const string reactionContent = ":k:";
        var request = new PeerAddReactionRequest
        {
            OdinId = pippin.Identity,
            Request = new AddRemoteReactionRequest
            {
                File = targetFile.uploadResult.GlobalTransitIdFileIdentifier,
                Reaction = reactionContent
            }
        };

        //
        // Send the reaction
        //
        var addReactionResponse = await AppTransitClients.ReactionsFor(merryApp).AddReaction(request);
        Assert.That(addReactionResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppCan_SendReactionContent_ToAnonymousDrive_With_ReactPermission()
    {
        // Prep
        var pippin = await LoginAsOwner(Identities.Pippin);
        var merry = await LoginAsOwner(Identities.Merry);
        var merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitRead);

        var remoteDrive = TargetDrive.NewTargetDrive();
        await pippin.Admin.CreateDrive(remoteDrive, "Some target drive", allowAnonymousReads: true);

        // Pippin uploads file
        var targetFile = await UploadStandardRandomPublicFileHeader(pippin, remoteDrive);

        //
        // Ensure the flag that allows authenticated identities to react is true
        //
        await pippin.Admin.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.AuthenticatedIdentitiesCanReactOnAnonymousDrives, true.ToString());

        const string reactionContent = ":k:";
        var request = new PeerAddReactionRequest
        {
            OdinId = pippin.Identity,
            Request = new AddRemoteReactionRequest
            {
                File = targetFile.uploadResult.GlobalTransitIdFileIdentifier,
                Reaction = reactionContent
            }
        };

        //
        // Send the reaction
        //
        var addReactionResponse = await AppTransitClients.ReactionsFor(merryApp).AddReaction(request);
        Assert.That(addReactionResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    [Ignore("this test cannot be finalized until we decide to support allowing authenticated identities to send data over transit")]
    public async Task AppCan_AddCommentOn_AnonymousDrive_With_CommentPermission()
    {
        // Prep
        var pippin = await LoginAsOwner(Identities.Pippin);
        var merry = await LoginAsOwner(Identities.Merry);
        var merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitWrite, PermissionKeys.UseTransitRead);

        var remoteDrive = TargetDrive.NewTargetDrive();
        await pippin.Admin.CreateDrive(remoteDrive, "Some target drive", allowAnonymousReads: true);

        // Pippin uploads file
        var targetFile = await UploadStandardRandomPublicFileHeader(pippin, remoteDrive);

        //
        // Ensure the flag that allows authenticated identities to comment is true
        //
        await pippin.Admin.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.AuthenticatedIdentitiesCanCommentOnAnonymousDrives, true.ToString());

        var recipients = new List<string> { pippin.Identity };
        var commentFileMetadata = new UploadFileMetadata
        {
            ReferencedFile = targetFile.uploadResult.GlobalTransitIdFileIdentifier,
            IsEncrypted = false,
            AppData = new UploadAppFileMetaData
            {
                FileType = 777,
                Content = "This is a Comment",
                UniqueId = Guid.NewGuid(),
            },
            AccessControlList = AccessControlList.Anonymous
        };

        //
        // Upload the comment via transit
        //
        var response = await AppTransitClients.TransferFileAsync(merryApp, commentFileMetadata, recipients,
            targetFile.uploadResult.File.TargetDrive, fileSystemType: FileSystemType.Comment);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await PeerFlow.DistributeAsync(merry, pippin, targetFile.uploadResult.File.TargetDrive);

        //
        // Get the comment on pippin's identity and test it
        //
        var remoteFile = new TransitExternalFileIdentifier
        {
            OdinId = pippin.Identity,
            File = targetFile.uploadResult.File
        };

        var getTransitFileHeaderResponse = await AppTransitClients.QueryFor(merryApp, FileSystemType.Comment).GetFileHeader(remoteFile);
        Assert.That(getTransitFileHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getTransitFileHeaderResponse.Content!.FileMetadata.AppData.Content, Is.EqualTo(commentFileMetadata.AppData.Content));
    }

    [Test]
    public async Task AppCan_AddCommentOn_AnonymousDrive_With_CommentPermission_and_ConnectedIdentity()
    {
        // Prep
        var pippin = await LoginAsOwner(Identities.Pippin);
        var merry = await LoginAsOwner(Identities.Merry);

        //Notice: no circles since we're only testing what can be done by connected identities on an anonymous drive
        var sendRequest = await pippin.Connections.SendConnectionRequest(merry.Identity, new List<GuidId>());
        Assert.That(sendRequest.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var accept = await merry.Connections.AcceptConnectionRequest(pippin.Identity, new List<GuidId>());
        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var remoteDrive = TargetDrive.NewTargetDrive();
        await pippin.Admin.CreateDrive(remoteDrive, "Some target drive", allowAnonymousReads: true);

        // Pippin uploads file
        var targetFile = await UploadStandardRandomPublicFileHeader(pippin, remoteDrive);

        //
        // Ensure the flag that allows authenticated identities to comment is true
        //
        await pippin.Admin.UpdateTenantSettingsFlag(
            TenantConfigFlagNames.AuthenticatedIdentitiesCanCommentOnAnonymousDrives, true.ToString());

        var recipients = new List<string> { pippin.Identity };
        var commentFileMetadata = new UploadFileMetadata
        {
            ReferencedFile = targetFile.uploadResult.GlobalTransitIdFileIdentifier,
            AllowDistribution = true,
            IsEncrypted = false,
            AppData = new UploadAppFileMetaData
            {
                FileType = 777,
                Content = "This is a Comment",
                UniqueId = Guid.NewGuid(),
            },
            AccessControlList = AccessControlList.Anonymous
        };

        //
        // Upload the comment via transit
        //
        var remoteTargetDrive = targetFile.uploadResult.File.TargetDrive;
        var merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitWrite, PermissionKeys.UseTransitRead);
        var response = await AppTransitClients.TransferFileAsync(merryApp, commentFileMetadata, recipients, remoteTargetDrive,
            fileSystemType: FileSystemType.Comment);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var transitResult = response.Content;
        Assert.That(transitResult, Is.Not.Null);
        Assert.That(transitResult!.RecipientStatus[pippin.Identity], Is.EqualTo(TransferStatus.Enqueued));

        await PeerFlow.DistributeAsync(merry, pippin, remoteTargetDrive);

        //
        // Merry uses transit query to get all files of that file type
        //
        var request = new PeerQueryBatchRequest
        {
            OdinId = pippin.Identity,
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = remoteTargetDrive,
                ClientUniqueIdAtLeastOne = [commentFileMetadata.AppData.UniqueId.GetValueOrDefault()]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                IncludeMetadataHeader = true,
                MaxRecords = 10,
                Ordering = QueryBatchSortOrder.NewestFirst,
                Sorting = QueryBatchSortField.CreatedDate
            }
        };

        var getTransitBatchResponse = await AppTransitClients.QueryFor(merryApp, FileSystemType.Comment).GetBatch(request);
        Assert.That(getTransitBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getTransitBatchResponse.Content, Is.Not.Null);

        var theRemoteComment = getTransitBatchResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(theRemoteComment, Is.Not.Null);
        Assert.That(theRemoteComment!.FileMetadata.AppData.Content, Is.EqualTo(commentFileMetadata.AppData.Content));
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The original's <c>UploadStandardRandomPublicFileHeader</c>: metadata only, anonymous ACL. It
    /// took payload / thumbnail parameters no caller in this fixture ever passed, so they are not
    /// carried.
    /// </summary>
    private static Task<(UploadResult uploadResult, UploadFileMetadata uploadedMetadata)> UploadStandardRandomPublicFileHeader(
        OwnerSession owner, TargetDrive targetDrive) =>
        AppTransitUploads.UploadStandardRandomFileAsync(owner, targetDrive, AccessControlList.Anonymous);
}
