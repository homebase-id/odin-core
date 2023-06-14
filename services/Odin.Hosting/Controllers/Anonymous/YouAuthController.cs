#nullable enable
using System;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Tenant;
using Odin.Hosting.Authentication.ClientToken;

namespace Odin.Hosting.Controllers.Anonymous
{
    [ApiController]
    [Route(YouAuthApiPathConstants.AuthV1)]
    public class YouAuthController : Controller
    {
        private readonly IYouAuthService _youAuthService;
        private readonly string _currentTenant;

        public YouAuthController(ITenantProvider tenantProvider, IYouAuthService youAuthService)
        {
            _currentTenant = tenantProvider.GetCurrentTenant()!.Name;
            _youAuthService = youAuthService;
        }

        //

        [HttpGet(YouAuthApiPathConstants.ValidateAuthorizationCodeRequestMethodName)]
        public async Task<ActionResult> ValidateAuthorizationCodeRequest(
            [FromQuery(Name = YouAuthDefaults.Subject)]
            string subject,
            [FromQuery(Name = YouAuthDefaults.AuthorizationCode)]
            string authorizationCode,
            [FromQuery(Name = YouAuthDefaults.ReturnUrl)]
            string returnUrl)
        {
            var (success, remoteIcrClientAuthToken) = await _youAuthService.ValidateAuthorizationCodeRequest(_currentTenant, subject, authorizationCode);

            if (!success)
            {
                var problemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Invalid code",
                    Instance = HttpContext.Request.Path
                };
                return new ObjectResult(problemDetails)
                {
                    ContentTypes = { "application/problem+json" },
                    StatusCode = problemDetails.Status,
                };
            }

            var clientAccessToken = await _youAuthService.RegisterBrowserAccess(subject, remoteIcrClientAuthToken);
            AuthenticationCookieUtil.SetCookie(Response, YouAuthDefaults.XTokenCookieName, clientAccessToken.ToAuthenticationToken());

            //TODO: RSA Encrypt shared secret
            var sharedSecret64 = Convert.ToBase64String(clientAccessToken?.SharedSecret.GetKey() ?? Array.Empty<byte>());
            clientAccessToken?.Wipe();

            // SEB:NOTE before brigde-hack:
            //var handlerUrl = $"/home/youauth/finalize?ss64={HttpUtility.UrlEncode(sharedSecret64)}&returnUrl={HttpUtility.UrlEncode(returnUrl)}";

            var handlerUrl = $"https://{Request.Host}{YouAuthApiPathConstants.FinalizeBridgeRequestRequestPath}?ss64={HttpUtility.UrlEncode(sharedSecret64)}&returnUrl={HttpUtility.UrlEncode(returnUrl)}";
            return Redirect(handlerUrl);
        }

        //

        [HttpGet(YouAuthApiPathConstants.FinalizeBridgeRequestMethodName)]
        public ActionResult FinalizeBridgeRequest(
            [FromQuery(Name = YouAuthDefaults.SharedSecret)]
            string ss64,
            [FromQuery(Name = YouAuthDefaults.ReturnUrl)]
            string returnUrl)
        {
            var handlerUrl = $"https://{_currentTenant}/home/youauth/finalize?ss64={HttpUtility.UrlEncode(ss64)}&returnUrl={HttpUtility.UrlEncode(returnUrl)}";
            return Redirect(handlerUrl);
        }

        //

        [HttpGet(YouAuthApiPathConstants.IsAuthenticatedMethodName)]
        [Produces("application/json")]
        [Authorize(AuthenticationSchemes = ClientTokenConstants.YouAuthScheme, Policy = ClientTokenPolicies.IsIdentified)]
        public ActionResult IsAuthenticated()
        {
            return Ok(true);
        }

        //

        [HttpGet(YouAuthApiPathConstants.DeleteTokenMethodName)]
        [Produces("application/json")]
        [Authorize(AuthenticationSchemes = ClientTokenConstants.YouAuthScheme)]
        public async Task<ActionResult> DeleteToken()
        {
            if (User?.Identity?.Name != null)
            {
                await _youAuthService.DeleteSession(User.Identity.Name);
            }

            Response.Cookies.Delete(YouAuthDefaults.XTokenCookieName);
            return Ok();
        }

        //

        [HttpGet(YouAuthApiPathConstants.PingMethodName)]
        [Produces("application/json")]
        [Authorize(AuthenticationSchemes = ClientTokenConstants.YouAuthScheme, Policy = ClientTokenPolicies.IsIdentified)]
        public string GetPing([FromQuery] string text)
        {
            return $"ping from {_currentTenant}: {text}";
        }

        //

    }
}
