using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.ClientToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1)]
    [Route(YouAuthApiPathConstants.DrivesV1)]
    [AuthorizeValidExchangeGrant]
    public class DriveAttachmentsController : DriveStorageControllerBase
    {
        public DriveAttachmentsController(FileSystemResolver fileSystemResolver, ITransitService transitService) : base(fileSystemResolver, transitService)
        {
        }

 
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/attachments/delete")]
        public async Task<DeleteAttachmentsResult> DeleteAttachment(DeleteAttachmentRequest request)
        {
            return await base.DeleteAttachment(request);
        }

    }
}