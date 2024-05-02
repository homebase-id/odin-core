﻿using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    [ApiController]
    [Route(OwnerApiPathConstants.DriveV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveMetadataController : OdinControllerBase
    {
        private readonly DriveManager _driveManager;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public OwnerDriveMetadataController(DriveManager driveManager, TenantSystemStorage tenantSystemStorage)
        {
            _driveManager = driveManager;
            _tenantSystemStorage = tenantSystemStorage;
        }

        /// <summary>
        /// Gets a list of drives by their type
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpGet("metadata/type")]
        public async Task<PagedResult<OwnerClientDriveData>> GetDrivesByType([FromQuery] GetDrivesByTypeRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            var drives = await _driveManager.GetDrives(request.DriveType, new PageOptions(request.PageNumber, request.PageSize), WebOdinContext, cn);

            var clientDriveData = drives.Results.Select(drive =>
                new OwnerClientDriveData()
                {
                    Name = drive.Name,
                    TargetDriveInfo = drive.TargetDriveInfo,
                    Metadata = drive.Metadata,
                    IsReadonly = drive.IsReadonly,
                    AllowAnonymousReads = drive.AllowAnonymousReads,
                    OwnerOnly = drive.OwnerOnly,
                    Attributes = drive.Attributes
                }).ToList();

            var page = new PagedResult<OwnerClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return page;
        }
    }
}