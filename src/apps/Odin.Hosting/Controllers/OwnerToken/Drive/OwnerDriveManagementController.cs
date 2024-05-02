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
            using var cn = _tenantSystemStorage.CreateConnection();
            var drives = await _driveManager.GetDrives(new PageOptions(request.PageNumber, request.PageSize), WebOdinContext, cn);

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

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("create")]
        public async Task<bool> CreateDrive([FromBody] CreateDriveRequest request)
        {
            //create a drive on the drive service
            using var cn = _tenantSystemStorage.CreateConnection();
            var _ = await _driveManager.CreateDrive(request, WebOdinContext, cn);
            return true;
        }

        [HttpPost("updatemetadata")]
        public async Task<bool> UpdateDriveMetadata([FromBody] UpdateDriveDefinitionRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            var driveId = await _driveManager.GetDriveIdByAlias(request.TargetDrive, cn, true);
            await _driveManager.UpdateMetadata(driveId.GetValueOrDefault(), request.Metadata, WebOdinContext, cn);
            return true;
        }

        [HttpPost("setdrivereadmode")]
        public async Task<IActionResult> SetDriveReadMode([FromBody] UpdateDriveReadModeRequest request)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            var driveId = await _driveManager.GetDriveIdByAlias(request.TargetDrive, cn, true);
            await _driveManager.SetDriveReadMode(driveId.GetValueOrDefault(), request.AllowAnonymousReads, WebOdinContext, cn);
            return Ok();
        }


        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpGet("type")]
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
                    AllowAnonymousReads = drive.AllowAnonymousReads
                }).ToList();

            var page = new PagedResult<OwnerClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return page;
        }
    }

    public class UpdateDriveDefinitionRequest
    {
        public TargetDrive TargetDrive { get; set; }

        public string Metadata { get; set; }
    }

    public class UpdateDriveReadModeRequest
    {
        public TargetDrive TargetDrive { get; set; }
        public bool AllowAnonymousReads { get; set; }
    }
}