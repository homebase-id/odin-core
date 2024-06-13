using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken.Shared;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.Reactions.DTOs;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query.Sqlite;
using Odin.Services.Drives.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Reactions;

namespace Odin.Hosting.Controllers.Reactions;

#nullable enable

// SEB:TODO remove all old controllers and DTOs

public abstract class ReactionController(
    ReactionContentService reactionContentService,
    PeerReactionSenderService peerReactionSenderService,
    TenantSystemStorage tenantSystemStorage
    ) : OdinControllerBase
{
    //

    [HttpPost("add")]
    public async Task<IActionResult> AddReactionContent([FromBody] AddReactionRequest2 request)
    {
        if (request.AuthorOdinId == WebOdinContext.Tenant) // Local
        {
            var file = new ExternalFileIdentifier
            {
                FileId = request.FileId,
                TargetDrive = request.TargetDrive
            };
            using var cn = tenantSystemStorage.CreateConnection();
            await reactionContentService.AddReaction(MapToInternalFile(file), request.Reaction, WebOdinContext, cn);
        }
        else // Remote
        {
            var remoteRequest = new AddRemoteReactionRequest
            {
                Reaction = request.Reaction,
                File = new GlobalTransitIdFileIdentifier
                {
                    GlobalTransitId = request.GlobalTransitId,
                    TargetDrive = request.TargetDrive
                }
            };
            using var cn = tenantSystemStorage.CreateConnection();
            await peerReactionSenderService.AddReaction(request.AuthorOdinId, remoteRequest, WebOdinContext, cn);
        }
        return NoContent();
    }

    //

    [HttpPost("delete")]
    public async Task<IActionResult> DeleteReactionContent([FromBody] DeleteReactionRequest2 request)
    {
        if (request.AuthorOdinId == WebOdinContext.Tenant) // Local
        {
            var file = new ExternalFileIdentifier
            {
                FileId = request.FileId,
                TargetDrive = request.TargetDrive
            };
            using var cn = tenantSystemStorage.CreateConnection();
            await reactionContentService.DeleteReaction(MapToInternalFile(file), request.Reaction, WebOdinContext, cn);
        }
        else // Remote
        {
            var remoteRequest = new DeleteReactionRequestByGlobalTransitId
            {
                Reaction = request.Reaction,
                File = new GlobalTransitIdFileIdentifier
                {
                    GlobalTransitId = request.GlobalTransitId,
                    TargetDrive = request.TargetDrive
                }
            };
            using var cn = tenantSystemStorage.CreateConnection();
            await peerReactionSenderService.DeleteReaction(request.AuthorOdinId, remoteRequest, WebOdinContext, cn);
        }
        return NoContent();
    }

    //

    // SEB:NOTE not called by frontend
    [HttpPost("deleteall")]
    public async Task<IActionResult> DeleteAllReactionsOnFile([FromBody] DeleteReactionRequest2 request)
    {
        if (request.AuthorOdinId == WebOdinContext.Tenant) // Local
        {
            var file = new ExternalFileIdentifier
            {
                FileId = request.FileId,
                TargetDrive = request.TargetDrive
            };
            using var cn = tenantSystemStorage.CreateConnection();
            await reactionContentService.DeleteAllReactions(MapToInternalFile(file), WebOdinContext, cn);
        }
        else // Remote
        {
            var remoteRequest = new DeleteReactionRequestByGlobalTransitId
            {
                File = new GlobalTransitIdFileIdentifier
                {
                    GlobalTransitId = request.GlobalTransitId,
                    TargetDrive = request.TargetDrive
                }
            };
            using var cn = tenantSystemStorage.CreateConnection();
            await peerReactionSenderService.DeleteAllReactions(request.AuthorOdinId, remoteRequest, WebOdinContext, cn);
        }
        return NoContent();
    }

    //

    [HttpPost("list")]
    public async Task<GetReactionsResponse2> GetAllReactions2([FromBody] GetReactionsRequest2 request)
    {
        if (request.AuthorOdinId == WebOdinContext.Tenant) // Local
        {
            var file = new ExternalFileIdentifier
            {
                FileId = request.FileId,
                TargetDrive = request.TargetDrive
            };
            using var cn = tenantSystemStorage.CreateConnection();
            var reactions = await reactionContentService.GetReactions(
                MapToInternalFile(file),
                cursor: request.Cursor,
                maxCount: request.MaxRecords,
                WebOdinContext,
                cn);
            return new GetReactionsResponse2
            {
                Reactions = reactions.Reactions,
                Cursor = reactions.Cursor
            };
        }
        else // Remote
        {
            var remoteRequest = new GetRemoteReactionsRequest
            {
                Cursor = request.Cursor,
                MaxRecords = request.MaxRecords,
                File = new GlobalTransitIdFileIdentifier
                {
                    GlobalTransitId = request.GlobalTransitId,
                    TargetDrive = request.TargetDrive
                }
            };
            using var cn = tenantSystemStorage.CreateConnection();
            var reactions = await peerReactionSenderService.GetReactions(request.AuthorOdinId, remoteRequest, WebOdinContext, cn);
            return new GetReactionsResponse2
            {
                // NOTE GetReactionsResponse and GetReactionsPerimeterResponse are not compatible, so we map
                // the parts that are required by the frontend
                Reactions = reactions.Reactions.Select(x => new Reaction {OdinId = x.OdinId, ReactionContent = x.ReactionContent}).ToList(),
                Cursor = reactions.Cursor
            };
        }
    }

    //

    [HttpPost("summary")]
    public async Task<GetReactionCountsResponse2> GetReactionCountsByFile([FromBody] GetReactionsRequest2 request)
    {
        if (request.AuthorOdinId == WebOdinContext.Tenant) // Local
        {
            var file = new ExternalFileIdentifier
            {
                FileId = request.FileId,
                TargetDrive = request.TargetDrive
            };
            using var cn = tenantSystemStorage.CreateConnection();
            var reactionCounts = await reactionContentService.GetReactionCountsByFile(MapToInternalFile(file), WebOdinContext, cn);
            return new GetReactionCountsResponse2
            {
                Reactions = reactionCounts.Reactions,
                Total = reactionCounts.Total
            };
        }
        else // Remote
        {
            var remoteRequest = new GetRemoteReactionsRequest
            {
                Cursor = request.Cursor,
                MaxRecords = request.MaxRecords,
                File = new GlobalTransitIdFileIdentifier
                {
                    TargetDrive = request.TargetDrive,
                    GlobalTransitId = request.GlobalTransitId
                }
            };
            using var cn = tenantSystemStorage.CreateConnection();
            var reactionCounts = await peerReactionSenderService.GetReactionCounts(request.AuthorOdinId, remoteRequest, WebOdinContext, cn);
            return new GetReactionCountsResponse2
            {
                Reactions = reactionCounts.Reactions,
                Total = reactionCounts.Total
            };
        }
    }

    //

    [HttpPost("listbyidentity")]
    public async Task<List<string>> GetReactionsByIdentityAndFile([FromBody] GetReactionsByIdentityRequest2 request)
    {
        if (request.AuthorOdinId == WebOdinContext.Tenant) // Local
        {
            var file = new ExternalFileIdentifier
            {
                FileId = request.FileId,
                TargetDrive = request.TargetDrive
            };
            using var cn = tenantSystemStorage.CreateConnection();
            return await reactionContentService.GetReactionsByIdentityAndFile(request.Identity, MapToInternalFile(file), WebOdinContext, cn);
        }
        else // Remote
        {
            var remoteRequest = new PeerGetReactionsByIdentityRequest
            {
                OdinId = request.AuthorOdinId, // remote identity server to query
                Identity = request.Identity, // the identity whose reactions we want
                File = new GlobalTransitIdFileIdentifier
                {
                    GlobalTransitId = request.GlobalTransitId,
                    TargetDrive = request.TargetDrive
                }
            };
            using var cn = tenantSystemStorage.CreateConnection();
            return await peerReactionSenderService.GetReactionsByIdentityAndFile(request.AuthorOdinId, remoteRequest, WebOdinContext, cn);
        }
    }

    //

}

//

[ApiController]
[AuthorizeValidOwnerToken]
[Route("/api/owner/v1/unified-reactions")] // SEB:TODO put this somewhere else
public class OwnerReactionController(
    ReactionContentService reactionContentService,
    PeerReactionSenderService peerReactionSenderService,
    TenantSystemStorage tenantSystemStorage)
    : ReactionController(reactionContentService, peerReactionSenderService, tenantSystemStorage);

//

[ApiController]
[AuthorizeValidGuestOrAppToken]
[Route("/api/apps/v1/unified-reactions")] // SEB:TODO put this somewhere else
public class AppReactionController(
    ReactionContentService reactionContentService,
    PeerReactionSenderService peerReactionSenderService,
    TenantSystemStorage tenantSystemStorage)
    : ReactionController(reactionContentService, peerReactionSenderService, tenantSystemStorage);

//

[ApiController]
[AuthorizeValidGuestOrAppToken]
[Route("/api/guest/v1/unified-reactions")] // SEB:TODO put this somewhere else
public class GuestReactionController(
    ReactionContentService reactionContentService,
    PeerReactionSenderService peerReactionSenderService,
    TenantSystemStorage tenantSystemStorage)
    : ReactionController(reactionContentService, peerReactionSenderService, tenantSystemStorage);

//
