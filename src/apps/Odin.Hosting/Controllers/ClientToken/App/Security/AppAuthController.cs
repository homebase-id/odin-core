#nullable enable
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authorization.Apps;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Base;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.App.Security
{
    [ApiController]
    [Route(AppApiPathConstants.AuthV1)]
    [AuthorizeValidAppToken]
    public class AppAuthController(
        IAppRegistrationService appRegistrationService,
        TenantSystemStorage tenantSystemStorage
        ) : OdinControllerBase
    {
        /// <summary>
        /// Verifies the ClientAuthToken (provided as a cookie) is Valid.
        /// </summary>
        /// <returns></returns>
        [HttpGet("verifytoken")]
        [SwaggerResponse((int)HttpStatusCode.OK, "Returned when ClientAuthToken is valid")]
        [SwaggerResponse((int)HttpStatusCode.Forbidden, "Returned when ClientAuthToken is not valid, expired, or revoked")]
        public ActionResult VerifyToken()
        {
            return Ok(true);
        }

        /// <summary>
        /// Deletes the client by it's access registration Id
        /// </summary>
        [HttpPost("logout")]
        public async Task DeleteClient()
        {
            // Cookie might have been set by the preauth middleware
            Response.Cookies.Delete(YouAuthConstants.AppCookieName);
            await appRegistrationService.DeleteCurrentAppClient(WebOdinContext);
        }
    }
}
