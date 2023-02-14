using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Controllers.ClientToken.Transit;

namespace Youverse.Hosting.Controllers.OwnerToken.Transit
{
    [ApiController]
    [Route(OwnerApiPathConstants.TransitV1 + "/inbox/processor")]
    [AuthorizeValidOwnerToken]
    public class TransitProcessController : ControllerBase
    {
        private readonly ITransitReceiverService _transitReceiverService;

        public TransitProcessController(ITransitReceiverService transitReceiverService)
        {
            _transitReceiverService = transitReceiverService;
        }

        [HttpPost("process")]
        public async Task<IActionResult> ProcessTransfers([FromBody] ProcessTransitInstructionRequest request)
        {
            if ((request.TargetDrive?.IsValid() ?? false) == false)
            {
                throw new YouverseClientException("Invalid target drive", YouverseClientErrorCode.InvalidTargetDrive);
            }

            await _transitReceiverService.ProcessIncomingTransitInstructions(request.TargetDrive);
            return new JsonResult(true);
        }
    }
}