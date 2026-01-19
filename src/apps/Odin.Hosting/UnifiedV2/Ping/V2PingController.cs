#nullable enable
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Ping
{
    [ApiController]
    [Route(UnifiedApiRouteConstants.Health)]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2PingController() : OdinControllerBase
    {
        [HttpGet("ping")]
        [SwaggerOperation(Tags = [SwaggerInfo.Health])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public JsonResult PingReply()
        {
            var reply = new JsonResult(new
            {
                identity = Request.Host.Host
            });

            return reply;
        }
    }
}