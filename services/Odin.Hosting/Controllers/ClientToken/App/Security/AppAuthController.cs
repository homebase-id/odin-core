#nullable enable
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authorization.Apps;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.App.Security
{
    [ApiController]
    [Route(AppApiPathConstants.AuthV1)]
    [AuthorizeValidAppExchangeGrant]
    public class AppAuthController : Controller
    {
        private readonly IAppRegistrationService _appRegistrationService;

        public AppAuthController(IAppRegistrationService appRegistrationService)
        {
            _appRegistrationService = appRegistrationService;
        }


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
            await _appRegistrationService.DeleteCurrentAppClient();
        }
    }
}