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
    [Route(UnifiedApiRouteConstants.FilesRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveCreateFileController(
        ILogger<V2DriveCreateFileController> logger,
        DriveManager driveManager,
        FileSystemResolver fileSystemResolver)
        : V1DriveUploadControllerBase(logger, driveManager, fileSystemResolver)
    {
        /// <summary>
        /// Uploads a new file to the drive using multipart form data
        /// </summary>
        [SwaggerOperation(
            Summary = "Create a new file",
            Description = "Uploads a new file using multipart/form-data.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        [Consumes("multipart/form-data")]
        [DisableFormValueModelBinding]
        [HttpPost]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public async Task<CreateFileResult> CreateNewFile()
        {
            var driveId = Guid.Parse(RouteData.Values["driveId"]!.ToString()!);
            OdinValidationUtils.AssertNotEmptyGuid(driveId, "missing drive id");

            var v2Result = await ReceiveNewFileStreamV2(driveId);
            return new CreateFileResult
            {
                FileId = v2Result.File.FileId,
                DriveId = v2Result.File.TargetDrive.Alias.Value,
                GlobalTransitId = v2Result.GlobalTransitId,
                RecipientStatus = v2Result.RecipientStatus,
                NewVersionTag = v2Result.NewVersionTag
            };
        }
    }
}