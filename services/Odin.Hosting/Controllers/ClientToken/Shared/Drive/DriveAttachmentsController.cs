using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.DriveV1)]
    [Route(GuestApiPathConstants.DriveV1)]
    [AuthorizeValidGuestOrAppToken]
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
        [HttpPost("files/attachments/deletepayload")]
        public async Task<DeletePayloadResult> DeletePayloadC(DeletePayloadRequest request)
        {
            return await base.DeletePayload(request);
        }
    }
}