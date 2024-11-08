using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    [ApiController]
    [Route(OwnerApiPathConstants.DriveManagementV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveManagementController : OdinControllerBase
    {
        private readonly DriveManager _driveManager;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public OwnerDriveManagementController(DriveManager driveManager, TenantSystemStorage tenantSystemStorage)
        {
            _driveManager = driveManager;
            _tenantSystemStorage = tenantSystemStorage;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost]
        public async Task<PagedResult<OwnerClientDriveData>> GetDrives([FromBody] GetDrivesRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var drives = await _driveManager.GetDrivesAsync(new PageOptions(request.PageNumber, request.PageSize), WebOdinContext, db);

            var clientDriveData = drives.Results.Select(drive =>
                new OwnerClientDriveData()
                {
                    Name = drive.Name,
                    TargetDriveInfo = drive.TargetDriveInfo,
                    Metadata = drive.Metadata,
                    IsReadonly = drive.IsReadonly,
                    AllowAnonymousReads = drive.AllowAnonymousReads,
                    AllowSubscriptions = drive.AllowSubscriptions,
                    OwnerOnly = drive.OwnerOnly,
                    Attributes = drive.Attributes
                }).ToList();

            var page = new PagedResult<OwnerClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return page;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("create")]
        public async Task<bool> CreateDrive([FromBody] CreateDriveRequest request)
        {
            //create a drive on the drive service
            var db = _tenantSystemStorage.IdentityDatabase;
            var _ = await _driveManager.CreateDriveAsync(request, WebOdinContext, db);
            return true;
        }

        [HttpPost("updatemetadata")]
        public async Task<bool> UpdateDriveMetadata([FromBody] UpdateDriveDefinitionRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var driveId = await _driveManager.GetDriveIdByAliasAsync(request.TargetDrive, db, true);
            await _driveManager.UpdateMetadataAsync(driveId.GetValueOrDefault(), request.Metadata, WebOdinContext, db);
            return true;
        }

        [HttpPost("UpdateAttributes")]
        public async Task<bool> UpdateDriveAttributes([FromBody] UpdateDriveDefinitionRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var driveId = await _driveManager.GetDriveIdByAliasAsync(request.TargetDrive, db, true);
            await _driveManager.UpdateAttributesAsync(driveId.GetValueOrDefault(), request.Attributes, WebOdinContext, db);
            return true;
        }

        [HttpPost("setdrivereadmode")]
        public async Task<IActionResult> SetDriveReadMode([FromBody] UpdateDriveReadModeRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var driveId = await _driveManager.GetDriveIdByAliasAsync(request.TargetDrive, db, true);
            await _driveManager.SetDriveReadModeAsync(driveId.GetValueOrDefault(), request.AllowAnonymousReads, WebOdinContext, db);
            return Ok();
        }
        
        [HttpPost("set-allow-subscriptions")]
        public async Task<IActionResult> SetDriveAllowSubscriptions([FromBody] UpdateDriveAllowSubscriptionsRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var driveId = await _driveManager.GetDriveIdByAliasAsync(request.TargetDrive, db, true);
            await _driveManager.SetDriveAllowSubscriptionsAsync(driveId.GetValueOrDefault(), request.AllowSubscriptions, WebOdinContext, db);
            return Ok();
        }
        

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpGet("type")]
        public async Task<PagedResult<OwnerClientDriveData>> GetDrivesByType([FromQuery] GetDrivesByTypeRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var drives = await _driveManager.GetDrivesAsync(request.DriveType, new PageOptions(request.PageNumber, request.PageSize), WebOdinContext, db);
            var clientDriveData = drives.Results.Select(drive =>
                new OwnerClientDriveData()
                {
                    Name = drive.Name,
                    TargetDriveInfo = drive.TargetDriveInfo,
                    Metadata = drive.Metadata,
                    IsReadonly = drive.IsReadonly,
                    AllowAnonymousReads = drive.AllowAnonymousReads,
                    AllowSubscriptions = drive.AllowSubscriptions,
                    OwnerOnly = drive.OwnerOnly,
                    Attributes = drive.Attributes
                }).ToList();

            var page = new PagedResult<OwnerClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return page;
        }
    }

    public class UpdateDriveDefinitionRequest
    {
        public TargetDrive TargetDrive { get; set; }

        public string Metadata { get; set; }

        public Dictionary<string, string> Attributes { get; set; }
    }

    public class UpdateDriveReadModeRequest
    {
        public TargetDrive TargetDrive { get; set; }
        public bool AllowAnonymousReads { get; set; }
    }
    
    public class UpdateDriveAllowSubscriptionsRequest
    {
        public TargetDrive TargetDrive { get; set; }
        public bool AllowSubscriptions { get; set; }
    }
}
