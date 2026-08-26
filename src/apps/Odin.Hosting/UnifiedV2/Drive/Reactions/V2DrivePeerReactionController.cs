using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Reactions;

/// <summary>
/// Reads reactions on a file hosted by another (peer) identity — the "over peer" twin of
/// <see cref="V2DriveGroupReactionController"/>'s read actions. Without these, a client can only
/// read its own local copy of a followed identity's post, which holds nothing but the reactions it
/// sent itself; the post's own count (from the file header) is authoritative and disagrees.
///
/// Writes already go over peer via <c>GroupReactionService</c> → outbox → the perimeter controller,
/// and are deliberately not duplicated here.
/// </summary>
/// <remarks>
/// Reactions are stored per (driveId, fileId) with no file-system-type discriminator, and the
/// remote resolves Standard-vs-Comment itself from the gtid
/// (<c>FileSystemResolver.ResolveFileSystem(GlobalTransitIdFileIdentifier, ...)</c> probes Standard
/// then Comment). There is no file-system-type field on the peer wire format, so unlike the local
/// group controller these actions do not forward <c>GetHttpFileSystemResolver().GetFileSystemType()</c>
/// — the caller's header would have nowhere to go and no effect on the answer.
/// </remarks>
[ApiController]
[Route(UnifiedApiRouteConstants.PeerReactionsByGtid)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2DrivePeerReactionController(PeerReactionSenderService peerReactionSenderService) : OdinControllerBase
{
    /// <summary>
    /// Lists the reactions on the peer's file.
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    [HttpGet]
    public async Task<GetReactionsResponse> GetAllReactions(
        [FromRoute] string odinId,
        [FromRoute] Guid driveId,
        [FromRoute] Guid gtid,
        [FromQuery] string cursor = null,
        [FromQuery] int maxRecords = 100)
    {
        AssertIsValidOdinId(odinId, out var id);

        var result = await peerReactionSenderService.GetReactionsAsync(id, new GetRemoteReactionsRequest
        {
            File = ToGtidFile(driveId, gtid),
            Cursor = cursor,
            MaxRecords = maxRecords
        }, WebOdinContext);

        // Deliberately returns the local GetReactionsResponse shape rather than the perimeter's
        // GetReactionsPerimeterResponse, so a client can decode a peer file's reactions with the same
        // decoder it already uses for a local one. FileId has no meaning across identities -- the
        // caller does not have the file on its own drive -- so it is left zeroed.
        return new GetReactionsResponse
        {
            Reactions = result?.Reactions?.Select(r => new Reaction
            {
                OdinId = r.OdinId,
                ReactionContent = r.ReactionContent,
                Created = r.Created,
                FileId = default
            }).ToList() ?? [],
            Cursor = result?.Cursor
        };
    }

    /// <summary>
    /// Gets a summary of reactions for the peer's file. The cursor and max parameters are ignored.
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    [HttpGet("summary")]
    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(
        [FromRoute] string odinId,
        [FromRoute] Guid driveId,
        [FromRoute] Guid gtid)
    {
        AssertIsValidOdinId(odinId, out var id);

        return await peerReactionSenderService.GetReactionCountsAsync(id, new GetRemoteReactionsRequest
        {
            File = ToGtidFile(driveId, gtid)
        }, WebOdinContext);
    }

    /// <summary>
    /// Gets the reactions a single identity left on the peer's file.
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    [HttpGet("by-identity")]
    public async Task<List<string>> GetReactionsByIdentity(
        [FromRoute] string odinId,
        [FromRoute] Guid driveId,
        [FromRoute] Guid gtid,
        [FromQuery] string identity)
    {
        AssertIsValidOdinId(odinId, out var id);
        OdinValidationUtils.AssertIsValidOdinId(identity, out var reactingIdentity);

        return await peerReactionSenderService.GetReactionsByIdentityAndFileAsync(id, new PeerGetReactionsByIdentityRequest
        {
            OdinId = id,
            Identity = reactingIdentity,
            File = ToGtidFile(driveId, gtid)
        }, WebOdinContext);
    }

    // The remote resolves the drive by alias (matching the peer file-read endpoints), so Type is left empty.
    private static GlobalTransitIdFileIdentifier ToGtidFile(Guid driveId, Guid gtid)
    {
        return new GlobalTransitIdFileIdentifier
        {
            GlobalTransitId = gtid,
            TargetDrive = new TargetDrive { Alias = driveId, Type = Guid.Empty }
        };
    }
}
