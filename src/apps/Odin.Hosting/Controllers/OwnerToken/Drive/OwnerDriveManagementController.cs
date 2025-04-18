using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage.Gugga;
using Odin.Services.Drives.Management;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    [ApiController]
    [Route(OwnerApiPathConstants.DriveManagementV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveManagementController(DriveManager driveManager, Defragmenter defragmenter) : OdinControllerBase
    {
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost]
        public async Task<PagedResult<OwnerClientDriveData>> GetDrives([FromBody] GetDrivesRequest request)
        {
            
            var drives = await driveManager.GetDrivesAsync(new PageOptions(request.PageNumber, request.PageSize), WebOdinContext);

            var clientDriveData = drives.Results.Select(drive =>
                new OwnerClientDriveData()
                {
                    DriveId = drive.Id,
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
            
            var _ = await driveManager.CreateDriveAsync(request, WebOdinContext);
            return true;
        }

        [HttpPost("updatemetadata")]
        public async Task<bool> UpdateDriveMetadata([FromBody] UpdateDriveDefinitionRequest request)
        {
            
            var driveId = await driveManager.GetDriveIdByAliasAsync(request.TargetDrive, true);
            await driveManager.UpdateMetadataAsync(driveId.GetValueOrDefault(), request.Metadata, WebOdinContext);
            return true;
        }

        [HttpPost("UpdateAttributes")]
        public async Task<bool> UpdateDriveAttributes([FromBody] UpdateDriveDefinitionRequest request)
        {
            
            var driveId = await driveManager.GetDriveIdByAliasAsync(request.TargetDrive, true);
            await driveManager.UpdateAttributesAsync(driveId.GetValueOrDefault(), request.Attributes, WebOdinContext);
            return true;
        }

        [HttpPost("setdrivereadmode")]
        public async Task<IActionResult> SetDriveReadMode([FromBody] UpdateDriveReadModeRequest request)
        {
            
            var driveId = await driveManager.GetDriveIdByAliasAsync(request.TargetDrive, true);
            await driveManager.SetDriveReadModeAsync(driveId.GetValueOrDefault(), request.AllowAnonymousReads, WebOdinContext);
            return Ok();
        }
        
        [HttpPost("set-allow-subscriptions")]
        public async Task<IActionResult> SetDriveAllowSubscriptions([FromBody] UpdateDriveAllowSubscriptionsRequest request)
        {
            var driveId = await driveManager.GetDriveIdByAliasAsync(request.TargetDrive, true);
            await driveManager.SetDriveAllowSubscriptionsAsync(driveId.GetValueOrDefault(), request.AllowSubscriptions, WebOdinContext);
            return Ok();
        }
        

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpGet("type")]
        public async Task<PagedResult<OwnerClientDriveData>> GetDrivesByType([FromQuery] GetDrivesByTypeRequest request)
        {
            
            var drives = await driveManager.GetDrivesAsync(request.DriveType, new PageOptions(request.PageNumber, request.PageSize), WebOdinContext);
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
        [HttpPost("defrag")]
        public async Task<IActionResult> DefragDrive([FromBody] TargetDrive targetDrive)
        {
            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem();
            await defragmenter.DefragDrive(targetDrive, fs, WebOdinContext);
            return Ok();
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