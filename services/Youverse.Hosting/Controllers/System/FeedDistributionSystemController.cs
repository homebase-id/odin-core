﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.DataSubscription;
using Youverse.Hosting.Authentication.System;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Controllers.System
{
    /// <summary>
    /// Runs feed distribution from the system account
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.FollowersV1 + "/system/distribute")]
    [Authorize(Policy = SystemPolicies.IsSystemProcess, AuthenticationSchemes = SystemAuthConstants.SchemeName)]
    public class FeedDistributionSystemController : ControllerBase
    {
        private readonly FeedDriveDistributionRouter _distributionService;

        public FeedDistributionSystemController(FeedDriveDistributionRouter distributionService)
        {
            _distributionService = distributionService;
        }

        [HttpPost("files")]
        public async Task<bool> DistributeFiles(int batchSize = 10)
        {
            await _distributionService.DistributeQueuedMetadataItems(batchSize);
            return true;
        }

        [HttpPost("reactionpreview")]
        public async Task<bool> DistributeReactionPreviews()
        {
            await _distributionService.DistributeReactionPreviews();
            return true;
        }
    }
}