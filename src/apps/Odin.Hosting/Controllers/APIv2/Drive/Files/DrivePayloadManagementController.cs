using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.APIv2.Base;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.APIv2.Drive.Files
{
    /// <summary />
    [ApiController]
    [OdinAuthorizeRoute(RootApiRoutes.Owner | RootApiRoutes.Apps | RootApiRoutes.Guest)]
    public class DrivePayloadManagementController : OdinControllerBase
    {
        [SwaggerOperation(Tags = [ApiV2SwaggerLabels.FileManagement])]
        [HttpPatch(ApiV2PathConstants.AppendPayload)]
        public Task<UploadResult> AppendPayload()
        {
            return Task.FromResult(new UploadResult());
        }


        [SwaggerOperation(Tags = [ApiV2SwaggerLabels.FileManagement])]
        [HttpGet(ApiV2PathConstants.GetPayload)]
        public Task<UploadResult> GetPayload([FromQuery] GetFileRequestV2 request)
        {
            return Task.FromResult(new UploadResult());
        }


        [SwaggerOperation(Tags = [ApiV2SwaggerLabels.FileManagement])]
        [HttpGet(ApiV2PathConstants.GetThumb)]
        public Task<UploadResult> GetThumb([FromQuery] GetFileRequestV2 request)
        {
            return Task.FromResult(new UploadResult());
        }


        [SwaggerOperation(Tags = [ApiV2SwaggerLabels.FileManagement])]
        [HttpDelete(ApiV2PathConstants.DeletePayload)]
        public Task<UploadResult> DeletePayload()
        {
            return Task.FromResult(new UploadResult());
        }
    }
}