using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.APIv2.Base;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken.Shared;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.APIv2.Drive.Files
{
    /// <summary />
    [ApiController]
    [OdinRoute(RootApiRoutes.Owner | RootApiRoutes.Apps | RootApiRoutes.Guest)]
    [AuthorizeValidGuestOrAppToken]
    public class DriveFileManagementController() : OdinControllerBase
    {
        /// <summary>
        /// Uploads a file using multi-part form data
        /// </summary>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { "ApiV2 Drive" })]
        [HttpPost(ApiV2PathConstants.UploadFile)]
        public Task<UploadResult> UploadFile()
        {
            return Task.FromResult(new UploadResult()
            {
            });
        }

        [SwaggerOperation(Tags = new[] { "ApiV2 Drive" })]
        [HttpPost(ApiV2PathConstants.UploadPayload)]
        public Task<UploadResult> UploadPayload()
        {
            return Task.FromResult(new UploadResult()
            {
            });
        }

        [SwaggerOperation(Tags = new[] { "ApiV2 Drive" })]
        [HttpDelete(ApiV2PathConstants.DeletePayload)]
        public Task<UploadResult> DeletePayloadPayload()
        {
            return Task.FromResult(new UploadResult()
            {
            });
        }


        [SwaggerOperation(Tags = new[] { "ApiV2 Drive" })]
        [HttpPost(ApiV2PathConstants.SendReadReceipts)]
        public Task<UploadResult> SendReadReceipt(SendReadReceiptRequest request)
        {
            return Task.FromResult(new UploadResult()
            {
            });
        }


        [SwaggerOperation(Tags = new[] { "ApiV2 Drive" })]
        [HttpDelete(ApiV2PathConstants.DeleteFiles)]
        public Task<UploadResult> Delete([FromBody] DeleteFileRequestV2 request)
        {
            return Task.FromResult(new UploadResult()
            {
            });
        }

        [SwaggerOperation(Tags = new[] { "ApiV2 Drive" })]
        [HttpGet(ApiV2PathConstants.GetHeader)]
        public Task<UploadResult> GetHeader([FromQuery] GetFileRequestV2 request)
        {
            return Task.FromResult(new UploadResult()
            {
            });
        }

        [SwaggerOperation(Tags = new[] { "ApiV2 Drive" })]
        [HttpGet(ApiV2PathConstants.GetThumb)]
        public Task<UploadResult> GetThumb([FromQuery] GetFileRequestV2 request)
        {
            return Task.FromResult(new UploadResult()
            {
            });
        }

        [SwaggerOperation(Tags = new[] { "ApiV2 Drive" })]
        [HttpGet(ApiV2PathConstants.GetPayload)]
        public Task<UploadResult> GetPayload([FromQuery] GetFileRequestV2 request)
        {
            return Task.FromResult(new UploadResult()
            {
            });
        }

        // }
    }
}