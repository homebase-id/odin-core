using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.DataSubscription;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Controllers.OwnerToken;

namespace Odin.Hosting.Controllers.System
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
        public async Task<bool> DistributeFiles()
        {
            await _distributionService.DistributeQueuedMetadataItems();
            return true;
        }
    }
}