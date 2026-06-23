using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.LiveRelay;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.LiveRelay
{
    /// <summary>
    /// App-initiated live data sharing (hop 1). The app POSTs an opaque data point ("share my live
    /// GPS with these connected identities on this channel"); the server fans it out to each
    /// recipient's peer perimeter, fire-and-forget. Nothing is stored durably.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.LiveRelay)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2LiveRelayController(LiveRelayService liveRelayService) : OdinControllerBase
    {
        [HttpPost]
        [SwaggerOperation(Tags = [SwaggerInfo.Notifications])]
        public async Task<IActionResult> Relay([FromBody] LiveRelayRequest request)
        {
            await liveRelayService.RelayAsync(request, WebOdinContext);
            return NoContent();
        }
    }
}
