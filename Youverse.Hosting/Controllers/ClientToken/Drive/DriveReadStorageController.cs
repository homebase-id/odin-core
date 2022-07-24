using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

namespace Youverse.Hosting.Controllers.ClientToken.Drive
{
    /// <summary>
    /// Api endpoints for reading drives
    /// </summary>
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1)]
    [Route(YouAuthApiPathConstants.DrivesV1)]
    [AuthorizeValidExchangeGrant]
    public class DriveReadStorageController : ControllerBase
    {
        private readonly IAppService _appService;
        private readonly IDriveService _driveService;
        private readonly DotYouContextAccessor _contextAccessor;

        /// <inheritdoc />
        public DriveReadStorageController(DotYouContextAccessor contextAccessor, IDriveService driveService, IAppService appService)
        {
            _contextAccessor = contextAccessor;
            _driveService = driveService;
            _appService = appService;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpPost("files/header")]
        public async Task<IActionResult> GetFileHeader([FromBody] ExternalFileIdentifier request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive),
                FileId = request.FileId
            };
            var result = await _appService.GetClientEncryptedFileHeader(file);
            return new JsonResult(result);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpPost("files/payload")]
        public async Task<IActionResult> GetPayloadStream([FromBody] ExternalFileIdentifier request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive),
                FileId = request.FileId
            };

            var payload = await _driveService.GetPayloadStream(file);

            return new FileStreamResult(payload, "application/octet-stream");
        }
        
        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpPost("files/thumb")]
        public async Task<IActionResult> GetThumbnail([FromBody] GetThumbnailRequest request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.File.TargetDrive),
                FileId = request.File.FileId
            };

            //TODO: should i write headers indicating the content type for this thumbnail?
            // this.Response.Headers.Add("x-AppData-content-type", new StringValues(""));
            
            var payload = await _driveService.GetThumbnailPayloadStream(file, request.Width, request.Height);
            
            return new FileStreamResult(payload, "application/octet-stream");
        }
    }
}