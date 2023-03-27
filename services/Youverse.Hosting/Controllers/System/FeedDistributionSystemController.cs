using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.DataSubscription;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Authentication.System;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Controllers.System
{
    /// <summary>
    /// Runs feed distribution from the system account
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.FollowersV1 + "/system")]
    [Authorize(Policy = SystemPolicies.IsSystemProcess, AuthenticationSchemes = SystemAuthConstants.SchemeName)]
    public class FeedDistributionSystemController : ControllerBase
    {
        private readonly FeedDriveDataSubscriptionDistributionService _distributionService;

        public FeedDistributionSystemController(FeedDriveDataSubscriptionDistributionService distributionService)
        {
            _distributionService = distributionService;
        }

        [HttpPost("distribute")]
        public async Task<bool> ProcessDistribution()
        {
            await _distributionService.DistributeItems();
            return true;
        }
    }
}