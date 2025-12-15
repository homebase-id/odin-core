using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Management;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive
{
    public sealed class DisableFormValueModelBindingAttribute :
        Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            context.HttpContext.Features.Set<IFormFeature>(null);
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }


    [ApiController]
    // [Route(UnifiedApiRouteConstants.FilesRoot)]
    [Route(UnifiedApiRouteConstants.DrivesRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    [DisableFormValueModelBinding]
    public class V2DriveUploadController(ILogger<V2DriveUploadController> logger, DriveManager driveManager)
        : V1DriveUploadControllerBase(logger, driveManager)
    {
        /// <summary>
        /// Uploads a new file to the drive using multipart form data
        /// </summary>
        /// <response code="200">File uploaded successfully.</response>
        [HttpPost("files/upload")]
        [SwaggerOperation(
            Summary = "Create a new file",
            Description = "Uploads a new file using multipart/form-data.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        [Consumes("multipart/form-data")]
        public async Task<UploadResult> Upload()
        {
            return await ReceiveNewFileStream();
        }

        /// <summary>
        /// Updates a file using multipart form data
        /// </summary>
        /// <returns></returns>
        [HttpPatch("files/upload")]
        [SwaggerOperation(
            Summary = "Updates an existing file",
            Description = "",
            Tags = [SwaggerInfo.FileWrite]
        )]
        [Consumes("multipart/form-data")]
        public async Task<FileUpdateResult> Update()
        {
            return await ReceiveFileUpdate();
        }
    }
}