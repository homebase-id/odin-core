using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.Drives.Reactions;
using Odin.Services.Drives.Reactions.Redux.Group;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.Base.Drive.GroupReactions;

/// <summary />
public abstract class DriveGroupReactionControllerBase : OdinControllerBase
{
    private const string SwaggerSection = "Group Reactions";
    private readonly GroupReactionService _groupReactionService;
    private readonly TenantSystemStorage _tenantSystemStorage;

    /// <summary />
    public DriveGroupReactionControllerBase(GroupReactionService groupReactionService, TenantSystemStorage tenantSystemStorage)
    {
        _groupReactionService = groupReactionService;
        _tenantSystemStorage = tenantSystemStorage;
    }

    /// <summary>
    /// Adds a reaction for a given file
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerSection])]
    [HttpPost]
    public async Task<AddReactionResult> AddReactionContent([FromBody] AddReactionRequestRedux request)
    {
        

        return await _groupReactionService.AddReactionAsync(request.File, request.Reaction, request.TransitOptions, WebOdinContext,
            this.GetHttpFileSystemResolver().GetFileSystemType());
    }

    /// <summary>
    /// Adds a reaction for a given file
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerSection])]
    [HttpDelete]
    public async Task<DeleteReactionResult> DeleteReactionContent([FromBody] DeleteReactionRequestRedux request)
    {
        

        return await _groupReactionService.DeleteReactionAsync(request.File, request.Reaction, request.TransitOptions, WebOdinContext,
            this.GetHttpFileSystemResolver().GetFileSystemType());
    }

    /// <summary />
    [SwaggerOperation(Tags = [SwaggerSection])]
    [HttpGet]
    public async Task<GetReactionsResponse> GetAllReactions([FromQuery] GetReactionsRequestRedux request)
    {
        
        return await _groupReactionService.GetReactionsAsync(request.File, request.Cursor, request.MaxRecords, WebOdinContext,
            this.GetHttpFileSystemResolver().GetFileSystemType());
    }

    /// <summary>
    /// Get reactions by identity and file
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerSection])]
    [HttpGet("by-identity")]
    public async Task<List<string>> GetReactionsByIdentity([FromQuery] GetReactionsByIdentityRequestRedux request)
    {
        

        OdinValidationUtils.AssertIsValidOdinId(request.Identity, out var identity);
        return await _groupReactionService.GetReactionsByIdentityAndFileAsync(identity, request.File, WebOdinContext,
            this.GetHttpFileSystemResolver().GetFileSystemType());
    }

    /// <summary>
    /// Gets a summary of reactions for the file.  The cursor and max parameters are ignored
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerSection])]
    [HttpGet("summary")]
    public async Task<GetReactionCountsResponse> GetReactionCountsByFile([FromQuery] GetReactionsRequestRedux request)
    {
        
        return await _groupReactionService.GetReactionCountsByFileAsync(request.File, WebOdinContext,
            this.GetHttpFileSystemResolver().GetFileSystemType());
    }
}