using System.IO;
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

        /// <summary>
        /// Returns the file header
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/header")]
        public async Task<IActionResult> GetFileHeader([FromBody] ExternalFileIdentifier request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive),
                FileId = request.FileId
            };
            var result = await _appService.GetClientEncryptedFileHeader(file);

            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        /// <summary>
        /// Returns the payload for a given file
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/payload")]
        public async Task<IActionResult> GetPayloadStream([FromBody] ExternalFileIdentifier request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive),
                FileId = request.FileId
            };

            var payload = await _driveService.GetPayloadStream(file);
            if (payload == Stream.Null)
            {
                return NotFound();
            }
            return new FileStreamResult(payload, "application/octet-stream");
        }
        
        /// <summary>
        /// Returns the thumbnail matching the width and height.  Note: you should get the content type from the file header
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/thumb")]
        public async Task<IActionResult> GetThumbnail([FromBody] GetThumbnailRequest request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.File.TargetDrive),
                FileId = request.File.FileId
            };

            var payload = await _driveService.GetThumbnailPayloadStream(file, request.Width, request.Height);
            if (payload == Stream.Null)
            {
                return NotFound();
            }
            return new FileStreamResult(payload, "application/octet-stream");
        }
    }
}