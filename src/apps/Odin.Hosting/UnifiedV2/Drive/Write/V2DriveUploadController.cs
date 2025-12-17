using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives.Management;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Write
{

    [ApiController]
    [Route(UnifiedApiRouteConstants.DrivesRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveUploadController(ILogger<V2DriveUploadController> logger, DriveManager driveManager)
        : V1DriveUploadControllerBase(logger, driveManager)
    {
        /// <summary>
        /// Uploads a new file to the drive using multipart form data
        /// </summary>
        [HttpPost("files")]
        [SwaggerOperation(
            Summary = "Create a new file",
            Description = "Uploads a new file using multipart/form-data.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        [Consumes("multipart/form-data")]
        public async Task<CreateFileResult> Upload()
        {
            var v1Result = await ReceiveNewFileStream();
            return new CreateFileResult
            {
                FileId = v1Result.File.FileId,
                DriveId = v1Result.File.TargetDrive.Alias.Value,
                GlobalTransitId = v1Result.GlobalTransitId,
                RecipientStatus = v1Result.RecipientStatus,
                NewVersionTag = v1Result.NewVersionTag
            };
        }

        /// <summary>
        /// Updates a file using multipart form data
        /// </summary>
        /// <returns></returns>
        [HttpPatch("files")]
        [SwaggerOperation(
            Summary = "Updates an existing file",
            Description = "",
            Tags = [SwaggerInfo.FileWrite]
        )]
        [Consumes("multipart/form-data")]
        public async Task<UpdateFileResult> Update()
        {
            var v1Result =  await ReceiveFileUpdate();
            return new UpdateFileResult
            {
                FileId = v1Result.File.FileId,
                DriveId = v1Result.File.TargetDrive.Alias.Value,
                GlobalTransitId = v1Result.GlobalTransitId,
                RecipientStatus = v1Result.RecipientStatus,
                NewVersionTag = v1Result.NewVersionTag
            };
        }
    }
}