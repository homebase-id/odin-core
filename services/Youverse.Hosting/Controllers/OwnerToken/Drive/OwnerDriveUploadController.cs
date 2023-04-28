using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload.Attachments;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary/>
    [ApiController]
    [Route(OwnerApiPathConstants.DriveStorageV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveUploadController : DriveUploadControllerBase
    {
        /// <summary/>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("upload")]
        public async Task<UploadResult> Upload()
        {
            return await base.ReceiveFileStream();
        }

        /// <summary>
        /// Adds a thumbnail to an existing file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("attachments/upload")]
        public async Task<UploadAttachmentsResult> AddAttachment()
        {
            return await base.ReceiveAttachmentStream();
        }
    }
}