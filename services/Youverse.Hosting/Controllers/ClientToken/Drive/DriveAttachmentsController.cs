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
        [HttpPost("files/attachments/deletethumbnail")]
        public async Task<DeleteThumbnailResult> DeleteThumbnailC(DeleteThumbnailRequest request)
        {
            return await base.DeleteThumbnail(request);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/attachments/deletepayload")]
        public async Task<DeletePayloadResult> DeletePayloadC(DeletePayloadRequest request)
        {
            return await base.DeletePayload(request);
        }
    }
}