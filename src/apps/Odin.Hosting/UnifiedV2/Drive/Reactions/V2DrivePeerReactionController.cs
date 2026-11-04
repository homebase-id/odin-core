using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Reactions;

/// <summary>
/// Reactions on a file another identity hosts, sent through this identity: the V2 twin of V1's
/// <c>transit/reactions</c>. The remote knows the file by the globalTransitId both copies share, and resolves the
/// drive by alias alone (<c>DriveQueryServiceBase.ResolveFileId</c>), so the type is left empty. The bodies are the
/// local reaction route's (<see cref="V2DriveReactionController"/>); the file comes from the path.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.PeerReactionsByGtid)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2DrivePeerReactionController(PeerReactionSenderService peerReactionSenderService) : OdinControllerBase
{
    [HttpPost("add")]
    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    public async Task<IActionResult> AddReaction(string odinId, Guid driveId, Guid gtid, [FromBody] AddReactionRequest request)
    {
        AssertIsValidOdinId(odinId, out var id);
        await peerReactionSenderService.AddReactionAsync(id,
            new AddRemoteReactionRequest { File = File(driveId, gtid), Reaction = request.Reaction }, WebOdinContext);
        return NoContent();
    }

    [HttpPost("delete")]
    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    public async Task<IActionResult> DeleteReaction(string odinId, Guid driveId, Guid gtid,
        [FromBody] DeleteReactionRequest request)
    {
        AssertIsValidOdinId(odinId, out var id);
        await peerReactionSenderService.DeleteReactionAsync(id,
            new DeleteReactionRequestByGlobalTransitId { File = File(driveId, gtid), Reaction = request.Reaction }, WebOdinContext);
        return NoContent();
    }

    /// <summary>Deletes all of this identity's reactions on the file; the body's reaction is ignored.</summary>
    [HttpPost("deleteall")]
    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    public async Task<IActionResult> DeleteAllReactions(string odinId, Guid driveId, Guid gtid)
    {
        AssertIsValidOdinId(odinId, out var id);
        await peerReactionSenderService.DeleteAllReactionsAsync(id,
            new DeleteReactionRequestByGlobalTransitId { File = File(driveId, gtid) }, WebOdinContext);
        return NoContent();
    }

    [HttpPost("list")]
    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    public async Task<GetReactionsPerimeterResponse> GetReactions(string odinId, Guid driveId, Guid gtid,
        [FromBody] GetReactionsRequest request)
    {
        AssertIsValidOdinId(odinId, out var id);
        return await peerReactionSenderService.GetReactionsAsync(id, new GetRemoteReactionsRequest
        {
            File = File(driveId, gtid),
            Cursor = request.Cursor,
            MaxRecords = request.MaxRecords
        }, WebOdinContext);
    }

    /// <summary>Counts of each reaction on the file. The cursor and max parameters are ignored.</summary>
    [HttpPost("summary")]
    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    public async Task<GetReactionCountsResponse> GetReactionSummary(string odinId, Guid driveId, Guid gtid)
    {
        AssertIsValidOdinId(odinId, out var id);
        return await peerReactionSenderService.GetReactionCountsAsync(id,
            new GetRemoteReactionsRequest { File = File(driveId, gtid) }, WebOdinContext);
    }

    [HttpPost("listbyidentity")]
    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    public async Task<List<string>> GetReactionsByIdentity(string odinId, Guid driveId, Guid gtid,
        [FromBody] GetReactionsByIdentityRequest request)
    {
        AssertIsValidOdinId(odinId, out var id);
        return await peerReactionSenderService.GetReactionsByIdentityAndFileAsync(id, new PeerGetReactionsByIdentityRequest
        {
            OdinId = id,
            Identity = request.Identity,
            File = File(driveId, gtid)
        }, WebOdinContext);
    }

    private static GlobalTransitIdFileIdentifier File(Guid driveId, Guid gtid) => new()
    {
        GlobalTransitId = gtid,
        TargetDrive = new TargetDrive { Alias = driveId, Type = Guid.Empty }
    };
}
