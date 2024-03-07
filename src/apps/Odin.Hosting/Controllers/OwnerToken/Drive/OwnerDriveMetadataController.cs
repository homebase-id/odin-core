﻿using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Base.SharedTypes;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Peer;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    [ApiController]
    [Route(OwnerApiPathConstants.DriveV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveMetadataController : ControllerBase
    {
        private readonly DriveManager _driveManager;

        public OwnerDriveMetadataController(DriveManager driveManager)
        {
            _driveManager = driveManager;
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
            var drives = await _driveManager.GetDrives(request.DriveType, new PageOptions(request.PageNumber, request.PageSize));

            var clientDriveData = drives.Results.Select(drive =>
                new OwnerClientDriveData()
                {
                    Name = drive.Name,
                    TargetDriveInfo = drive.TargetDriveInfo,
                    Metadata = drive.Metadata,
                    IsReadonly = drive.IsReadonly,
                    AllowAnonymousReads = drive.AllowAnonymousReads,
                    OwnerOnly = drive.OwnerOnly
                }).ToList();

            var page = new PagedResult<OwnerClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return page;
        }
    }
}