using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Drive.Comment;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.Base;
using Youverse.Hosting.Controllers.Base.Upload.Comment;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive.Comments
{
    [ApiController]
    [Route(OwnerApiPathConstants.DriveCommentsV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveCommentUploadController : DriveUploadControllerBase
    {
        private readonly CommentFileUploadService _fileUploadService;

        public OwnerDriveCommentUploadController(CommentFileUploadService fileUploadService)
        {
            _fileUploadService = fileUploadService;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("upload")]
        public async Task<UploadResult> Upload()
        {
            return await base.ReceiveStream(_fileUploadService);
        }
    }
}