using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Transit;

namespace Youverse.Hosting.Controllers.ClientToken.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.TransitV1 + "/app")]
    [AuthorizeValidAppExchangeGrant]
    public class TransitAppController : ControllerBase
    {
        private readonly ITransitAppService _transitAppService;

        public TransitAppController(ITransitAppService transitAppService)
        {
            _transitAppService = transitAppService;
        }

        [HttpPost("process")]
        public async Task<IActionResult> ProcessTransfers()
        {
            await _transitAppService.ProcessTransfers();
            return new JsonResult(true);
        }

        [HttpGet]
        public Task<JsonResult> GetQuarantinedItems(int pageNumber, int pageSize)
        {
            try
            {
                return null;
            }
            catch (InvalidDataException e)
            {
                return Task.FromResult(new JsonResult(new NoResultResponse(false, e.Message)));
            }
        }
    }
}