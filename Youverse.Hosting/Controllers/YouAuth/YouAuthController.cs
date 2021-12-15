using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Tenant;
using Youverse.Core.Util;
using Youverse.Hosting.Security.Authentication.YouAuth;

#nullable enable
namespace Youverse.Hosting.Controllers.YouAuth
{
    [ApiController]
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

        [HttpGet(YouAuthDefaults.CreateTokenFlowPath)]
        [Produces("application/json")]
        [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)] // SEB:TODO this does 302 on access denied. It should do 403.
        public async Task<ActionResult> CreateTokenFlow([FromQuery(Name = YouAuthDefaults.ReturnUrl)]string returnUrl)
        {
            if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out Uri? uri))
            {
                return BadRequest($"Missing or bad returnUrl '{returnUrl}'");
            }

            var initiator = uri.Host;
            var subject = _currentTenant;

            var authorizationCode = await _youAuthService.CreateAuthorizationCode(initiator, subject);

            var queryString = QueryString.Create(new Dictionary<string, string?>()
            {
                {YouAuthDefaults.AuthorizationCode, authorizationCode},
                {YouAuthDefaults.Subject, subject},
                {YouAuthDefaults.ReturnUrl, returnUrl},
            });

            var redirectUrl = $"https://{initiator}".UrlAppend(
                YouAuthDefaults.ValidateAuthorizationCodeRequestPath,
                queryString.ToUriComponent());

            return new JsonResult(new { redirectUrl });
        }

        //

        [HttpGet(YouAuthDefaults.ValidateAuthorizationCodeRequestPath)]
        public async Task<ActionResult> ValidateAuthorizationCodeRequest(
            [FromQuery(Name = YouAuthDefaults.Subject)]string subject,
            [FromQuery(Name = YouAuthDefaults.AuthorizationCode)]string authorizationCode,
            [FromQuery(Name = YouAuthDefaults.ReturnUrl)]string returnUrl)
        {
            var success = await _youAuthService.ValidateAuthorizationCodeRequest(_currentTenant, subject, authorizationCode);

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

            var session = await _youAuthService.CreateSession(subject);

            var options = new CookieOptions()
            {
                HttpOnly = true,
                IsEssential = true,
                Secure = true,
                SameSite = SameSiteMode.Strict
            };

            Response.Cookies.Append(YouAuthDefaults.CookieName, session.Id.ToString(), options);

            return Redirect(returnUrl);
        }

        //

        [HttpGet(YouAuthDefaults.ValidateAuthorizationCodeResponsePath)]
        [Produces("application/json")]
        public async Task<ActionResult> ValidateAuthorizationCodeResponse(
            [FromQuery(Name = YouAuthDefaults.Initiator)] string initiator,
            [FromQuery(Name = YouAuthDefaults.AuthorizationCode)] string authorizationCode)
        {
            var success = await _youAuthService.ValidateAuthorizationCode(initiator, authorizationCode);

            if (success)
            {
                return new ObjectResult("ok");
            }

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

        //

        [HttpGet(YouAuthDefaults.IsAuthenticated)]
        [Produces("application/json")]
        [Authorize(AuthenticationSchemes = YouAuthAuthenticationDefaults.Scheme)]
        public ActionResult IsAuthenticated()
        {
            return Ok(true);
        }

        //

        [HttpGet(YouAuthDefaults.DeleteTokenPath)]
        [Produces("application/json")]
        [Authorize(AuthenticationSchemes = YouAuthAuthenticationDefaults.Scheme)]
        public async Task<ActionResult> DeleteToken()
        {
            if (User?.Identity?.Name != null)
            {
                await _youAuthService.DeleteSession(User.Identity.Name);
            }
            Response.Cookies.Delete(YouAuthDefaults.CookieName);
            return Ok();
        }

        //

        [HttpGet("/home/youauth/test/resource")]
        [Produces("application/json")]
        [Authorize(AuthenticationSchemes = YouAuthAuthenticationDefaults.Scheme)]
        public ActionResult TestResource()
        {
            var subject = User?.Identity?.Name;
            return new ObjectResult($"Hey {subject}! Here's my secret!");
        }

        //
    }
}