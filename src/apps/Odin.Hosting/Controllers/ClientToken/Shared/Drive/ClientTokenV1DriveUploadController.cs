using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Services.Drives.Management;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstantsV1.DriveV1)]
    [Route(GuestApiPathConstantsV1.DriveV1)]
    [AuthorizeValidGuestOrAppToken]
    public class ClientTokenV1DriveUploadController(ILogger<ClientTokenV1DriveUploadController> logger, DriveManager driveManager)
        : V1DriveUploadControllerBase(logger, driveManager)
    {
        
        /// <summary>
        /// Uploads a file using multi-part form data
        /// </summary>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/upload")]
        public async Task<UploadResult> Upload()
        {
            return await base.ReceiveNewFileStream();
        }
        
        /// <summary>
        /// Adds an attachment (thumbnail or payload) to an existing file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/uploadpayload")]
        public async Task<UploadPayloadResult> UploadPayloadOnly()
        {
            
            return await base.ReceivePayloadStream();
        }
    }
}