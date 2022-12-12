#nullable enable
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Tenant;

namespace Youverse.Hosting.Controllers.ClientToken.App
{
    [ApiController]
    [Route(AppApiPathConstants.AuthV1)]
    [AuthorizeValidAppExchangeGrant]
    public class AppAuthController : Controller
    {
        private readonly string _currentTenant;

        public AppAuthController(ITenantProvider tenantProvider)
        {
            _currentTenant = tenantProvider.GetCurrentTenant()!.Name;
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
    }
}