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
    public class DriveFileHeaderManagementController : OdinControllerBase
    {
        [SwaggerOperation(Tags = [ApiV2SwaggerLabels.FileManagement])]
        [HttpPatch(ApiV2PathConstants.UpdateHeader)]
        public Task<IActionResult> UpdateHeader()
        {
            return Task.FromResult(new OkResult() as IActionResult);
        }
        
        [SwaggerOperation(Tags = [ApiV2SwaggerLabels.FileManagement])]
        [HttpGet(ApiV2PathConstants.GetHeader)]
        public Task<UploadResult> GetHeader([FromQuery] GetFileRequestV2 request)
        {
            return Task.FromResult(new UploadResult()
            {
            });
        }


        [SwaggerOperation(Tags = [ApiV2SwaggerLabels.FileManagement])]
        [HttpDelete(ApiV2PathConstants.DeleteFile)]
        public Task<IActionResult> DeleteFile([FromBody] DeleteFileRequestV2 request)
        {
            return Task.FromResult(Ok() as IActionResult);
        }


        [SwaggerOperation(Tags = [ApiV2SwaggerLabels.FileManagement])]
        [HttpPost(ApiV2PathConstants.SendReadReceipts)]
        public Task<UploadResult> SendReadReceipt(SendReadReceiptRequest request)
        {
            return Task.FromResult(new UploadResult()
            {
            });
        }
    }
}