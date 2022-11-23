﻿#nullable enable
using System;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Tenant;
using Youverse.Hosting.Authentication.ClientToken;

namespace Youverse.Hosting.Controllers.Anonymous
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
                    ContentTypes = {"application/problem+json"},
                    StatusCode = problemDetails.Status,
                };
            }

            var clientAccessToken = await _youAuthService.RegisterBrowserAccess(subject, remoteIcrClientAuthToken);

            var options = new CookieOptions()
            {
                HttpOnly = true,
                IsEssential = true,
                Secure = true,
                SameSite = SameSiteMode.Strict
            };

            var authenticationToken = clientAccessToken.ToAuthenticationToken();

            Response.Cookies.Append(YouAuthDefaults.XTokenCookieName, authenticationToken.ToString(), options);

            //TODO: RSA Encrypt shared secret
            var shareSecret64 = Convert.ToBase64String(clientAccessToken?.SharedSecret.GetKey() ?? Array.Empty<byte>());
            clientAccessToken?.Wipe();

            var handlerUrl = $"/home/youauth/finalize?ss64={HttpUtility.UrlEncode(shareSecret64)}&returnUrl={HttpUtility.UrlEncode(returnUrl)}";
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
    }
}