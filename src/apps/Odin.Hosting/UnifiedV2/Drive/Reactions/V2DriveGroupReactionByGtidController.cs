using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions;
using Odin.Services.Drives.Reactions.Redux.Group;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Reactions;

/// <summary>
/// Group reactions on a file addressed by its globalTransitId. A chat message's reactions are keyed by
/// the gtid every recipient shares, and the client often holds only that, not its local fileId.
/// <see cref="V2DriveGroupReactionController"/> takes a fileId and looks the gtid up; here the gtid is
/// given, so the service is called directly.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.GroupReactionsByGtid)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2DriveGroupReactionByGtidController(GroupReactionService groupReactionService) : OdinControllerBase
{
    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    [HttpPost]
    public async Task<AddReactionResult> AddReactionContent(Guid driveId, Guid gtid, [FromBody] AddReactionRequestRedux request)
    {
        return await groupReactionService.AddReactionAsync(File(driveId, gtid), request.Reaction, request.TransitOptions,
            WebOdinContext, this.GetHttpFileSystemResolver().GetFileSystemType());
    }

    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    [HttpDelete]
    public async Task<DeleteReactionResult> DeleteReactionContent(Guid driveId, Guid gtid,
        [FromBody] DeleteReactionRequestRedux request)
    {
        return await groupReactionService.DeleteReactionAsync(File(driveId, gtid), request.Reaction, request.TransitOptions,
            WebOdinContext, this.GetHttpFileSystemResolver().GetFileSystemType());
    }

    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    [HttpGet]
    public async Task<GetReactionsResponse> GetAllReactions(Guid driveId, Guid gtid, [FromQuery] GetReactionsRequestRedux request)
    {
        int.TryParse(request.Cursor, out var c);
        return await groupReactionService.GetReactionsAsync(File(driveId, gtid), c, request.MaxRecords, WebOdinContext,
            this.GetHttpFileSystemResolver().GetFileSystemType());
    }

    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    [HttpGet("by-identity")]
    public async Task<List<string>> GetReactionsByIdentity(Guid driveId, Guid gtid,
        [FromQuery] GetReactionsByIdentityRequestRedux request)
    {
        OdinValidationUtils.AssertIsValidOdinId(request.Identity, out var identity);
        return await groupReactionService.GetReactionsByIdentityAndFileAsync(identity, File(driveId, gtid), WebOdinContext,
            this.GetHttpFileSystemResolver().GetFileSystemType());
    }

    /// <summary>
    /// Gets a summary of reactions for the file.  The cursor and max parameters are ignored
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    [HttpGet("summary")]
    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(Guid driveId, Guid gtid,
        [FromQuery] GetReactionsRequestRedux request)
    {
        return await groupReactionService.GetReactionCountsByFileAsync(File(driveId, gtid), WebOdinContext,
            this.GetHttpFileSystemResolver().GetFileSystemType());
    }

    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    [HttpPost("toggle")]
    public async Task<ToggleReactionResult> ToggleReaction(Guid driveId, Guid gtid, [FromBody] ToggleReactionRequest request)
    {
        return await groupReactionService.ToggleReaction(File(driveId, gtid), request.Reaction, request.TransitOptions,
            WebOdinContext, this.GetHttpFileSystemResolver().GetFileSystemType());
    }

    private FileIdentifier File(Guid driveId, Guid gtid) => new()
    {
        GlobalTransitId = gtid,
        TargetDrive = WebOdinContext.PermissionsContext.GetTargetDrive(driveId)
    };
}
