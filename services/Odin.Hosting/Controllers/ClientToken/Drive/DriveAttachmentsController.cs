using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Transit.SendingHost;
using Odin.Hosting.Controllers.Anonymous;
using Odin.Hosting.Controllers.Base;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1)]
    [Route(YouAuthApiPathConstants.DrivesV1)]
    [AuthorizeValidExchangeGrant]
    public class DriveAttachmentsController : DriveStorageControllerBase
    {
        private readonly ILogger<DriveAttachmentsController> _logger;

        public DriveAttachmentsController(
            ILogger<DriveAttachmentsController> logger,
            FileSystemResolver fileSystemResolver,
            ITransitService transitService)
            : base(logger, fileSystemResolver, transitService)
        {
            _logger = logger;
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