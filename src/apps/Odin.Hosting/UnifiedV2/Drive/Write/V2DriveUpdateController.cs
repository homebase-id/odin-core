using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Write
{
    [ApiController]
    [Route(UnifiedApiRouteConstants.DrivesRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveUpdateController(
        ILogger<V2DriveUpdateController> logger,
        DriveManager driveManager,
        FileSystemResolver fileSystemResolver)
        : V1DriveUploadControllerBase(logger, driveManager, fileSystemResolver)
    {
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
        [DisableFormValueModelBinding]
        public async Task<UpdateFileResult> UpdateByFileId()
        {
            var driveId = Guid.Parse(RouteData.Values["driveId"]!.ToString()!);
            OdinValidationUtils.AssertNotEmptyGuid(driveId, "missing drive id");
            
            var fileId = Guid.Parse(RouteData.Values["fileId"]!.ToString()!);
            OdinValidationUtils.AssertNotEmptyGuid(driveId, "missing file id");
            
            var v1Result = await ReceiveFileUpdateV2(driveId, fileId);
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