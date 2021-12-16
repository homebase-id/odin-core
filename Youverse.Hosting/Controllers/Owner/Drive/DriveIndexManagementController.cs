using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Drive.Query;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner.Drive
{
    [ApiController]
    [Route("/api/owner/v1/drive/index")]
    [Authorize(Policy = OwnerPolicies.IsDigitalIdentityOwnerPolicyName, AuthenticationSchemes = OwnerAuthConstants.DotIdentityOwnerScheme)]
    public class DriveIndexManagementController : ControllerBase
    {
        private readonly IDriveMetadataIndexer _indexer;

        public DriveIndexManagementController(IDriveMetadataIndexer indexer)
        {
            _indexer = indexer;
        }
        
        [HttpPost("rebuildall")]
        public async Task<bool> RebuildAll()
        {
            await _indexer.RebuildAll();
            return true;
        }
        
        [HttpPost("rebuild")]
        public async Task<bool> Rebuild(Guid driveId)
        {
            await _indexer.Rebuild(driveId);
            return true;
        }
    }
}