using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Apps;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Reactions;
using Odin.Services.Drives.Reactions.Redux.Group;
using Odin.Services.Peer.Incoming.Reactions;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// The V2 routes added so odin-js's js-lib can leave V1: local header and group reactions by globalTransitId,
/// drives by type (local and over peer), the peer security context, the peer header by uniqueId, and reactions
/// on another identity's file.
///
/// Layout: Sam hosts the drive and uploads; Frodo is connected and granted Read and React on Sam's drive.
/// </summary>
[TestFixture]
public class V2JsLibGapRoutesTests : V2Fixture
{
    private const int FileType = 7021;

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    public interface IGapRoutes
    {
        [Get(UnifiedApiRouteConstants.ByGtid + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetHeaderByGtid(
            [AliasAs("driveId:guid")] Guid driveId, [AliasAs("gtid:guid")] Guid gtid);

        [Post(UnifiedApiRouteConstants.GroupReactionsByGtid)]
        Task<ApiResponse<AddReactionResult>> AddGroupReactionByGtid(
            [AliasAs("driveId:guid")] Guid driveId, [AliasAs("gtid:guid")] Guid gtid, [Body] AddReactionRequestRedux request);

        [Get(UnifiedApiRouteConstants.GroupReactionsByGtid)]
        Task<ApiResponse<GetReactionsResponse>> GetGroupReactionsByGtid(
            [AliasAs("driveId:guid")] Guid driveId, [AliasAs("gtid:guid")] Guid gtid, [Query] int maxRecords);

        [Get(UnifiedApiRouteConstants.DrivesRoot)]
        Task<ApiResponse<PagedResult<ClientDriveData>>> GetDrivesByType([Query] Guid type);

        [Get(UnifiedApiRouteConstants.PeerDrivesRoot)]
        Task<ApiResponse<PagedResult<ClientDriveData>>> GetPeerDrivesByType(string odinId, [Query] Guid type);

        [Get(UnifiedApiRouteConstants.PeerAuth + "/context")]
        Task<ApiResponse<RedactedOdinContext>> GetPeerSecurityContext(string odinId);

        [Get(UnifiedApiRouteConstants.PeerByUniqueId + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetPeerHeaderByUid(
            string odinId, [AliasAs("driveId:guid")] Guid driveId, [AliasAs("uid:guid")] Guid uid);

        [Post(UnifiedApiRouteConstants.PeerReactionsByGtid + "/add")]
        Task<ApiResponse<HttpContent>> AddPeerReaction(
            string odinId, [AliasAs("driveId:guid")] Guid driveId, [AliasAs("gtid:guid")] Guid gtid,
            [Body] AddReactionRequest request);

        [Post(UnifiedApiRouteConstants.PeerReactionsByGtid + "/delete")]
        Task<ApiResponse<HttpContent>> DeletePeerReaction(
            string odinId, [AliasAs("driveId:guid")] Guid driveId, [AliasAs("gtid:guid")] Guid gtid,
            [Body] DeleteReactionRequest request);

        [Post(UnifiedApiRouteConstants.PeerReactionsByGtid + "/list")]
        Task<ApiResponse<GetReactionsPerimeterResponse>> ListPeerReactions(
            string odinId, [AliasAs("driveId:guid")] Guid driveId, [AliasAs("gtid:guid")] Guid gtid,
            [Body] GetReactionsRequest request);

        [Post(UnifiedApiRouteConstants.PeerReactionsByGtid + "/summary")]
        Task<ApiResponse<GetReactionCountsResponse>> PeerReactionSummary(
            string odinId, [AliasAs("driveId:guid")] Guid driveId, [AliasAs("gtid:guid")] Guid gtid);

        [Post(UnifiedApiRouteConstants.PeerReactionsByGtid + "/listbyidentity")]
        Task<ApiResponse<List<string>>> PeerReactionsByIdentity(
            string odinId, [AliasAs("driveId:guid")] Guid driveId, [AliasAs("gtid:guid")] Guid gtid,
            [Body] GetReactionsByIdentityRequest request);
    }

    private static IGapRoutes Routes(OwnerSession session)
    {
        var (http, sharedSecret) = session.NewAdminHttpClient();
        return RefitCreator.RestServiceFor<IGapRoutes>(http, sharedSecret);
    }

    [Test]
    public async Task LocalHeaderByGtid_ReturnsTheFile_AndMissingIs404()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = TargetDrive.NewTargetDrive();
        await sam.Admin.CreateDrive(drive, "sam drive", allowAnonymousReads: false);
        var (_, gtid) = await UploadAsync(sam, drive);

        var header = await Routes(sam).GetHeaderByGtid(drive.Alias, gtid);
        Assert.That(header.IsSuccessStatusCode, Is.True, $"header by gtid: {header.StatusCode}");
        Assert.That(header.Content!.FileMetadata.GlobalTransitId, Is.EqualTo(gtid));

        var missing = await Routes(sam).GetHeaderByGtid(drive.Alias, Guid.NewGuid());
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GroupReactionByGtid_AddsAndLists()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = TargetDrive.NewTargetDrive();
        await sam.Admin.CreateDrive(drive, "sam drive", allowAnonymousReads: false);
        var (_, gtid) = await UploadAsync(sam, drive);

        var added = await Routes(sam).AddGroupReactionByGtid(drive.Alias, gtid,
            new AddReactionRequestRedux { Reaction = ":tada:", TransitOptions = null });
        Assert.That(added.IsSuccessStatusCode, Is.True, $"add group reaction by gtid: {added.StatusCode}");

        var list = await Routes(sam).GetGroupReactionsByGtid(drive.Alias, gtid, 10);
        Assert.That(list.IsSuccessStatusCode, Is.True, $"list group reactions by gtid: {list.StatusCode}");
        Assert.That(list.Content!.Reactions.Select(r => r.ReactionContent), Does.Contain(":tada:"));
    }

    [Test]
    public async Task DrivesByType_OwnerSeesNames_AnonymousSeesOnlyAddresses()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = TargetDrive.NewTargetDrive();
        await sam.Admin.CreateDrive(drive, "public drive", allowAnonymousReads: true);

        var asOwner = await Routes(sam).GetDrivesByType(drive.Type);
        Assert.That(asOwner.IsSuccessStatusCode, Is.True, $"owner drives by type: {asOwner.StatusCode}");
        var ownerDrive = asOwner.Content!.Results.Single();
        Assert.That(ownerDrive.TargetDrive.Alias, Is.EqualTo(drive.Alias));
        Assert.That(ownerDrive.Name, Is.EqualTo("public drive"));

        var anonymous = RestService.For<IGapRoutes>(Host.CreateAnonymousClient(Identities.Sam));
        var asAnonymous = await anonymous.GetDrivesByType(drive.Type);
        Assert.That(asAnonymous.IsSuccessStatusCode, Is.True, $"anonymous drives by type: {asAnonymous.StatusCode}");
        var anonymousDrive = asAnonymous.Content!.Results.Single();
        Assert.That(anonymousDrive.TargetDrive.Alias, Is.EqualTo(drive.Alias));
        Assert.That(anonymousDrive.Name, Is.Empty, "a non-owner must not see the drive's name");
        Assert.That(anonymousDrive.Attributes, Is.Null);
    }

    [Test]
    public async Task PeerDrivesByType_AndPeerSecurityContext()
    {
        var (frodo, sam, drive) = await ConnectAsync();

        var drives = await Routes(frodo).GetPeerDrivesByType(sam.Identity, drive.Type);
        Assert.That(drives.IsSuccessStatusCode, Is.True, $"peer drives by type: {drives.StatusCode}");
        Assert.That(drives.Content!.Results.Select(d => d.TargetDrive.Alias), Does.Contain(drive.Alias));

        var context = await Routes(frodo).GetPeerSecurityContext(sam.Identity);
        Assert.That(context.IsSuccessStatusCode, Is.True, $"peer security context: {context.StatusCode}");
        Assert.That(context.Content, Is.Not.Null);
    }

    [Test]
    public async Task PeerHeaderByUid_ReturnsTheFile_AndMissingIs404()
    {
        var (frodo, sam, drive) = await ConnectAsync();
        var uid = Guid.NewGuid();
        await UploadAsync(sam, drive, uid);

        var header = await Routes(frodo).GetPeerHeaderByUid(sam.Identity, drive.Alias, uid);
        Assert.That(header.IsSuccessStatusCode, Is.True, $"peer header by uid: {header.StatusCode}");
        Assert.That(header.Content!.FileMetadata.AppData.UniqueId, Is.EqualTo(uid));

        var missing = await Routes(frodo).GetPeerHeaderByUid(sam.Identity, drive.Alias, Guid.NewGuid());
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PeerReactions_AddListSummarizeAndDelete()
    {
        var (frodo, sam, drive) = await ConnectAsync();
        var (_, gtid) = await UploadAsync(sam, drive);
        var routes = Routes(frodo);

        var added = await routes.AddPeerReaction(sam.Identity, drive.Alias, gtid, new AddReactionRequest { Reaction = ":+1:" });
        Assert.That(added.IsSuccessStatusCode, Is.True, $"add peer reaction: {added.StatusCode}");

        var list = await routes.ListPeerReactions(sam.Identity, drive.Alias, gtid, new GetReactionsRequest { MaxRecords = 10 });
        Assert.That(list.IsSuccessStatusCode, Is.True, $"list peer reactions: {list.StatusCode}");
        Assert.That(list.Content!.Reactions.Select(r => r.ReactionContent), Does.Contain(":+1:"));

        var summary = await routes.PeerReactionSummary(sam.Identity, drive.Alias, gtid);
        Assert.That(summary.IsSuccessStatusCode, Is.True, $"peer reaction summary: {summary.StatusCode}");
        Assert.That(summary.Content!.Total, Is.EqualTo(1));

        var mine = await routes.PeerReactionsByIdentity(sam.Identity, drive.Alias, gtid,
            new GetReactionsByIdentityRequest { Identity = frodo.Identity });
        Assert.That(mine.IsSuccessStatusCode, Is.True, $"peer reactions by identity: {mine.StatusCode}");
        Assert.That(mine.Content, Does.Contain(":+1:"));

        var deleted = await routes.DeletePeerReaction(sam.Identity, drive.Alias, gtid, new DeleteReactionRequest { Reaction = ":+1:" });
        Assert.That(deleted.IsSuccessStatusCode, Is.True, $"delete peer reaction: {deleted.StatusCode}");

        var after = await routes.PeerReactionSummary(sam.Identity, drive.Alias, gtid);
        Assert.That(after.Content!.Total, Is.EqualTo(0));
    }

    // -----------------------------------------------------------------------------------------

    private async Task<(OwnerSession frodo, OwnerSession sam, TargetDrive drive)> ConnectAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read | DrivePermission.React,
            "gap routes", allowAnonymousReads: false);
        return (frodo, sam, drive);
    }

    private static async Task<(Guid fileId, Guid gtid)> UploadAsync(OwnerSession owner, TargetDrive drive, Guid? uniqueId = null)
    {
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var metadata = SampleMetadataData.Create(fileType: FileType, acl: AccessControlList.Connected);
        metadata.AppData.UniqueId = uniqueId;
        var manifest = new UploadManifest
        {
            PayloadDescriptors = new List<TestPayloadDefinition> { payload }.ToPayloadDescriptorList().ToList()
        };
        var uploaded = await owner.Drives.Writer.CreateNewUnencryptedFile(
            drive.Alias, metadata, manifest, new List<TestPayloadDefinition> { payload });
        Assert.That(uploaded.IsSuccessStatusCode, Is.True, $"upload failed: {uploaded.StatusCode}");
        var fileId = uploaded.Content!.FileId;

        var header = await owner.Drives.Reader.GetFileHeaderAsync(drive.Alias, fileId);
        var gtid = header.Content!.FileMetadata.GlobalTransitId
                   ?? throw new InvalidOperationException("stored file is expected to have a GlobalTransitId");
        return (fileId, gtid);
    }
}
