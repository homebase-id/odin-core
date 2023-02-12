using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers.Base;
using Youverse.Hosting.Controllers.Base.Upload.Standard;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

namespace Youverse.Hosting.Controllers.ClientToken.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1)]
    [AuthorizeValidExchangeGrant]
    public class DriveStorageWriteController : DriveUploadControllerBase
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IAppService _appService;

        private readonly StandardFileDriveUploadService _uploadService;

        public DriveStorageWriteController(DotYouContextAccessor contextAccessor, IDriveStorageService driveStorageService, ITransitService transitService,
            IAppService appService, StandardFileDriveUploadService uploadService)
        {
            _contextAccessor = contextAccessor;
            _appService = appService;
            _uploadService = uploadService;
        }

        /// <summary>
        /// Deletes a file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/delete")]
        public async Task<IActionResult> DeleteFile([FromBody] DeleteFileRequest request)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.File.TargetDrive);

            var file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = request.File.FileId
            };

            var result = await _appService.DeleteFile(file, request.Recipients);
            if (result.LocalFileNotFound)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        /// <summary>
        /// Uploads a file using multi-part form data
        /// </summary>
        /// <returns></returns>
        /// <exception cref="UploadException"></exception>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/upload")]
        public async Task<UploadResult> Upload()
        {
            return await base.ReceiveStream(_uploadService);
        }
    }
}