﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Drives.Reactions;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Services.Base;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.DriveReactionsV1)]
    [Route(GuestApiPathConstants.DriveReactionsV1)]
    [AuthorizeValidGuestOrAppToken]
    public class DriveReactionContentController : DriveReactionContentControllerBase
    {
        private readonly TenantSystemStorage _tenantSystemStorage;
        /// <summary />
        public DriveReactionContentController(ReactionContentService reactionContentService, TenantSystemStorage tenantSystemStorage) : base(reactionContentService)
        {
            _tenantSystemStorage = tenantSystemStorage;
        }

        /// <summary>
        /// Adds a reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("add")]
        public async Task<IActionResult> AddReactionContent([FromBody] AddReactionRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            await base.AddReaction(request, cn);
            return NoContent();
        }

        /// <summary>
        /// Adds a reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("delete")]
        public async Task<IActionResult> DeleteReactionContent([FromBody] DeleteReactionRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            await base.DeleteReaction(request, cn);
            return NoContent();
        }

        /// <summary>
        /// Adds a reaction for a given file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("deleteall")]
        public async Task<IActionResult> DeleteAllReactionsOnFile([FromBody] DeleteReactionRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            await base.DeleteAllReactions(request, cn);
            return NoContent();
        }


        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("list")]
        public async Task<GetReactionsResponse> GetAllReactions([FromBody] GetReactionsRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            return await base.GetReactions(request, cn);
        }

        /// <summary>
        /// Gets a summary of reactions for the file.  The cursor and max parameters are ignored
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("summary")]
        public async Task<GetReactionCountsResponse> GetReactionCountsByFile([FromBody] GetReactionsRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            return await base.GetReactionCounts(request, cn);
        }
        
        /// <summary>
        /// Get reactions by identity and file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("listbyidentity")]
        public async Task<List<string>> GetReactionsByIdentity([FromBody] GetReactionsByIdentityRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            return await base.GetReactionsByIdentityAndFile(request, cn);
        }
    }
}