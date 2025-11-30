using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives.Management;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Services.Base;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    [ApiController]
    [Route(AppApiPathConstantsV1.DriveV1)]
    [Route(GuestApiPathConstantsV1.DriveV1)]
    [AuthorizeValidGuestOrAppToken]
    public class ClientTokenDriveMetadataController : OdinControllerBase
    {
        private readonly IDriveManager _driveManager;


        public ClientTokenDriveMetadataController(IDriveManager driveManager)
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
            var drives = await _driveManager.GetDrivesAsync(request.DriveType, new PageOptions(request.PageNumber, request.PageSize),
                WebOdinContext);

            var clientDriveData = drives.Results.Select(drive => new ClientDriveData()
            {
                TargetDrive = drive.TargetDriveInfo,
                Name = WebOdinContext.Caller.IsOwner ? drive.Name : string.Empty,
                Attributes = WebOdinContext.Caller.IsOwner ? drive.Attributes : default
            }).ToList();

            var page = new PagedResult<ClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return page;
        }
    }
}