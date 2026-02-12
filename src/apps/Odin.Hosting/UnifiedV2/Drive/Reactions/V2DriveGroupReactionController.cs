using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Base;
using Odin.Services.Drives.Reactions;
using Odin.Services.Drives.Reactions.Redux.Group;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Reactions;

[ApiController]
[Route(UnifiedApiRouteConstants.GroupReactionsByFileId)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2DriveGroupReactionController(GroupReactionService groupReactionService) : OdinControllerBase
{
    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    [HttpPost]
    public async Task<AddReactionResult> AddReactionContent(Guid driveId, Guid fileId, [FromBody] AddReactionRequestRedux request)
    {
        var file = new FileIdentifier()
        {
            FileId = fileId,
            TargetDrive = WebOdinContext.PermissionsContext.GetTargetDrive(driveId)
        };
        return await groupReactionService.AddReactionAsync(file, request.Reaction, request.TransitOptions, WebOdinContext,
            this.GetHttpFileSystemResolver().GetFileSystemType());
    }

    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    [HttpDelete]
    public async Task<DeleteReactionResult> DeleteReactionContent(Guid driveId, Guid fileId, [FromBody] DeleteReactionRequestRedux request)
    {
        var file = new FileIdentifier()
        {
            FileId = fileId,
            TargetDrive = WebOdinContext.PermissionsContext.GetTargetDrive(driveId)
        };
        return await groupReactionService.DeleteReactionAsync(file, request.Reaction, request.TransitOptions, WebOdinContext,
            this.GetHttpFileSystemResolver().GetFileSystemType());
    }

    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    [HttpGet]
    public async Task<GetReactionsResponse> GetAllReactions(Guid driveId, Guid fileId, [FromQuery] GetReactionsRequestRedux request)
    {
        int.TryParse(request.Cursor, out var c);

        var file = new FileIdentifier()
        {
            FileId = fileId,
            TargetDrive = WebOdinContext.PermissionsContext.GetTargetDrive(driveId)
        };
        return await groupReactionService.GetReactionsAsync(file, c, request.MaxRecords, WebOdinContext,
            this.GetHttpFileSystemResolver().GetFileSystemType());
    }

    /// <summary>
    /// Get reactions by identity and file
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    [HttpGet("by-identity")]
    public async Task<List<string>> GetReactionsByIdentity(Guid driveId, Guid fileId,
        [FromQuery] GetReactionsByIdentityRequestRedux request)
    {
        OdinValidationUtils.AssertIsValidOdinId(request.Identity, out var identity);

        var file = new FileIdentifier()
        {
            FileId = fileId,
            TargetDrive = WebOdinContext.PermissionsContext.GetTargetDrive(driveId)
        };
        return await groupReactionService.GetReactionsByIdentityAndFileAsync(identity, file, WebOdinContext,
            this.GetHttpFileSystemResolver().GetFileSystemType());
    }

    /// <summary>
    /// Gets a summary of reactions for the file.  The cursor and max parameters are ignored
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerInfo.FileReaction])]
    [HttpGet("summary")]
    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(Guid driveId, Guid fileId,
        [FromQuery] GetReactionsRequestRedux request)
    {
        var file = new FileIdentifier()
        {
            FileId = fileId,
            TargetDrive = WebOdinContext.PermissionsContext.GetTargetDrive(driveId)
        };

        return await groupReactionService.GetReactionCountsByFileAsync(file, WebOdinContext,
            this.GetHttpFileSystemResolver().GetFileSystemType());
    }
}