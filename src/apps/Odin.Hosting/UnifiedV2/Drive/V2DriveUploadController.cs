using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive
{
    [ApiController]
    [Route(UnifiedApiRouteConstants.FilesRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveUploadController(ILogger logger) : V1DriveUploadControllerBase(logger)
    {
        /// <summary>
        /// Uploads a new file to the drive using multipart form data
        /// </summary>
        /// <response code="200">File uploaded successfully.</response>
        [HttpPost]
        [SwaggerOperation(
            Summary = "Create a new file",
            Description = "Uploads a new file using multipart/form-data.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        public async Task<UploadResult> Upload()
        {
            return await ReceiveNewFileStream();
        }

        /// <summary>
        /// Updates a file using multipart form data
        /// </summary>
        /// <returns></returns>
        [HttpPatch]
        [SwaggerOperation(
            Summary = "Updates an existing file",
            Description = "",
            Tags = [SwaggerInfo.FileWrite]
        )]
        public async Task<FileUpdateResult> Update()
        {
            return await ReceiveFileUpdate();
        }
    }
}