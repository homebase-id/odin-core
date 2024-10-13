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
    public class DriveReadReceiptController : OdinControllerBase
    {
        [SwaggerOperation(Tags = [ApiV2SwaggerLabels.FileManagement])]
        [HttpPost(ApiV2PathConstants.SendReadReceipts)]
        public Task<UploadResult> SendReadReceipt(SendReadReceiptRequest request)
        {
            return Task.FromResult(new UploadResult());
        }
    }
}