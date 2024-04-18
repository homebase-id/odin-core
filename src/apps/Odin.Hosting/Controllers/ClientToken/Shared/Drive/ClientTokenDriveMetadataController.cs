using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Odin.Hosting.ApiExceptions.Client;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DriveV1)]
    [Route(GuestApiPathConstants.DriveV1)]
    [AuthorizeValidGuestOrAppToken]
    public class ClientTokenDriveMetadataController : OdinControllerBase
    {
        private readonly DriveManager _driveManager;

        public ClientTokenDriveMetadataController(DriveManager driveManager)
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
        public async Task<PagedResult<ClientDriveData>> GetDrivesByType([FromQuery] GetDrivesByTypeRequest request)
        {
            //TODO: make logic centralized and match transitperimeterservice
            var drives = await _driveManager.GetDrives(request.DriveType, new PageOptions(request.PageNumber, request.PageSize), TheOdinContext);

            var clientDriveData = drives.Results.Select(drive =>
                new ClientDriveData()
                {
                    TargetDrive = drive.TargetDriveInfo,
                }).ToList();

            var page = new PagedResult<ClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return page;
        }
    }
}