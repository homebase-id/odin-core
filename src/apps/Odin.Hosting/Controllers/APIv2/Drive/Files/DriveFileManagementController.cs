using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.APIv2.Base;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.APIv2.Drive.Files
{
    /// <summary />
    [ApiController]
    [OdinAuthorizeRoute(RootApiRoutes.Owner | RootApiRoutes.Apps | RootApiRoutes.Guest)]
    public class DriveFileManagementController : OdinControllerBase
    {
        /// <summary>
        /// Uploads a file using multi-part form data
        /// </summary>
        /// <returns></returns>
        [SwaggerOperation(Tags = [ApiV2SwaggerLabels.FileManagement])]
        [HttpPost(ApiV2PathConstants.CreateFile)]
        public Task<UploadResult> CreateFile()
        {
            return Task.FromResult(new UploadResult()
            {
            });
        }
        
        [SwaggerOperation(Tags = [ApiV2SwaggerLabels.FileManagement])]
        [HttpPatch(ApiV2PathConstants.UpdateFile)]
        public Task<IActionResult> UpdateFile()
        {
            return Task.FromResult(new OkResult() as IActionResult);
        }

        [SwaggerOperation(Tags = [ApiV2SwaggerLabels.FileManagement])]
        [HttpDelete(ApiV2PathConstants.DeleteFile)]
        public Task<IActionResult> DeleteFile([FromBody] DeleteFileRequestV2 request)
        {
            return Task.FromResult(Ok() as IActionResult);
        }
        
    }
}