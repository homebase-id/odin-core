using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Base;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Security
{
    /// <summary>
    /// What the caller may do. Open to anonymous visitors, as V1's guest route is, so apart from
    /// <see cref="V2AuthController"/>, which requires a signed-in caller.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.Auth)]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2AuthContextController : OdinControllerBase
    {
        /// <summary>
        /// Returns redacted security information for the caller
        /// </summary>
        [HttpGet("context")]
        [SwaggerOperation(Tags = [SwaggerInfo.Auth])]
        public RedactedOdinContext GetSecurityContext()
        {
            return WebOdinContext.Redacted();
        }
    }
}
