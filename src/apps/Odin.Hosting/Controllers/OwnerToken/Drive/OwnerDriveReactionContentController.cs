﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives.Reactions;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Base;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveReactionsV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveReactionContentController : DriveReactionContentControllerBase
    {
        private readonly TenantSystemStorage _tenantSystemStorage;

        /// <summary />
        public OwnerDriveReactionContentController(ReactionContentService reactionContentService, TenantSystemStorage tenantSystemStorage) : base(reactionContentService)
        {
            _tenantSystemStorage = tenantSystemStorage;
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("add")]
        public async Task<IActionResult> AddReactionContent([FromBody] AddReactionRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            await base.AddReaction(request, cn);
            return NoContent();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("delete")]
        public async Task<IActionResult> DeleteReactionContent([FromBody] DeleteReactionRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            await base.DeleteReaction(request, cn);
            return NoContent();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("deleteall")]
        public async Task<IActionResult> DeleteAllReactionsOnFile([FromBody] DeleteReactionRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            await base.DeleteAllReactions(request, cn);
            return NoContent();
        }

        /// <summary />
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("list")]
        public async Task<GetReactionsResponse> GetAllReactions2([FromBody] GetReactionsRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            return await base.GetReactions(request, cn);
        }
        
        /// <summary>
        /// Gets a summary of reactions for the file.  The cursor and max parameters are ignored.
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("summary")]
        public async Task<GetReactionCountsResponse> GetReactionCountsByFile([FromBody] GetReactionsRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            return await base.GetReactionCounts(request, cn);
        }
        
        /// <summary>
        /// Get reactions by identity and file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("listbyidentity")]
        public async Task<List<string>> GetReactionsByIdentity([FromBody] GetReactionsByIdentityRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            return await base.GetReactionsByIdentityAndFile(request, cn);
        }
    }
}