using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner.Drive
{
    [ApiController]
    [Route("/api/owner/v1/drive/index")]
    [Authorize(Policy = OwnerPolicies.IsDigitalIdentityOwnerPolicyName, AuthenticationSchemes = OwnerAuthConstants.DotIdentityOwnerScheme)]
    public class DriveIndexManagementController : ControllerBase
    {
        private readonly IDriveService _driveService;

        public DriveIndexManagementController(IDriveService driveService)
        {
            _driveService = driveService;
        }
        
        [HttpPost("rebuildall")]
        public async Task<bool> RebuildAll()
        {
            await _driveService.RebuildAllIndices();
            return true;
        }
        
        [HttpPost("rebuild")]
        public async Task<bool> Rebuild(Guid driveId)
        {
            await _driveService.RebuildIndex(driveId);
            return true;
        }
    }
}