using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Incoming;
using Youverse.Hosting.Authentication.App;

namespace Youverse.Hosting.Controllers.Apps.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.TransitV1 + "/transitbox")]
    [Authorize(Policy = AppPolicies.IsAuthorizedApp, AuthenticationSchemes = AppAuthConstants.SchemeName)]
    public class TransitBoxController : ControllerBase
    {
        private readonly ITransitBoxService _transitBox;

        public TransitBoxController(ITransitService svc, ITransitBoxService transitBox)
        {
            _transitBox = transitBox;
        }
        
        [HttpGet]
        public async Task<IActionResult> GetList(int pageNumber, int pageSize)
        {
            var items = await _transitBox.GetPendingItems(new PageOptions(pageNumber, pageSize));
            return new JsonResult(items);
        }

        [HttpGet("item")]
        public async Task<IActionResult> GetItem(Guid id)
        {
            var items = await _transitBox.GetItem(id);
            return new JsonResult(items);
        }

        [HttpDelete("item")]
        public async Task<IActionResult> RemoveItem(Guid id)
        {
            await _transitBox.RemoveItem(id);
            return new JsonResult(true);
        }
    }
}