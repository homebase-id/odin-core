﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authorization.Apps;

namespace Youverse.Hosting.Controllers.Owner.AppManagement
{
    [ApiController]
    [Route(OwnerApiPathConstants.AppManagementDrivesV1)]
    [AuthorizeOwnerConsole]
    public class AppRegistrationDriveController : Controller
    {
        private readonly IAppRegistrationService _appRegistrationService;

        public AppRegistrationDriveController(IAppRegistrationService appRegistrationService)
        {
            _appRegistrationService = appRegistrationService;
        }

        [HttpGet("owned")]
        public async Task<IActionResult> GetOwnedDrives([FromQuery] int pageNumber, [FromQuery] int pageSize)
        {
            // await _appRegistrationService.CreateOwnedDrive(appId, publicDriveIdentifier, driveName);

            throw new NotImplementedException("");
        }
        
        [HttpPost("owned")]
        public async Task<IActionResult> CreateOwnedDrive(Guid appId, Guid publicDriveIdentifier, string driveName)
        {
            await _appRegistrationService.CreateOwnedDrive(appId, publicDriveIdentifier, driveName);
            return Ok();
        }

    }
}