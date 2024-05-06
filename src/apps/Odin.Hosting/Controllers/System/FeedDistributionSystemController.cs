using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.DataSubscription;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.System
{
    /// <summary>
    /// Runs feed distribution from the system account
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.FollowersV1 + "/system/distribute")]
    [Authorize(Policy = SystemPolicies.IsSystemProcess, AuthenticationSchemes = SystemAuthConstants.SchemeName)]
    public class FeedDistributionSystemController(
        FeedDriveDistributionRouter distributionService,
        TenantSystemStorage tenantSystemStorage) : OdinControllerBase
    {
        [HttpPost("files")]
        public async Task<bool> DistributeFiles()
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await distributionService.DistributeQueuedMetadataItems(WebOdinContext, cn);
            return true;
        }
    }
}