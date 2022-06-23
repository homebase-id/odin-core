using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Authentication.App;

namespace Youverse.Hosting.Controllers.Apps.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.TransitV1 + "/app")]
    [AuthorizeOwnerConsoleOrApp]
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