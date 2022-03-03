using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Authorization.Exchange;
using Youverse.Core.Services.Tenant;
using Youverse.Hosting.Authentication.YouAuth;

#nullable enable
namespace Youverse.Hosting.Controllers.YouAuth
{

    public class YouAuthFinalizationInfo
    {
        public byte[] SharedSecret { get; set; }

        /// <summary>
        /// The original Url to which a browser should be redirected after storing the Shared Secret;
        /// </summary>
        public string ReturnUrl { get; set; }

    }

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
            var (success, remoteKey) = await _youAuthService.ValidateAuthorizationCodeRequest(_currentTenant, subject, authorizationCode);

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

            var (session, sessionRemoteGrantKey, childSharedSecret) = await _youAuthService.CreateSession(subject, remoteKey?.ToSensitiveByteArray() ?? null);

            var options = new CookieOptions()
            {
                HttpOnly = true,
                IsEssential = true,
                Secure = true,
                SameSite = SameSiteMode.Strict
            };

            Response.Cookies.Append(YouAuthDefaults.SessionCookieName, session.Id.ToString(), options);
            if (null != sessionRemoteGrantKey)
            {
                Response.Cookies.Append(YouAuthDefaults.XTokenCookieName, Convert.ToBase64String(sessionRemoteGrantKey.GetKey()), options);
            }

            sessionRemoteGrantKey?.Wipe();
            childSharedSecret?.Wipe();

            //TODO: RSA Encrypt shared secret
            var finalInfo = new YouAuthFinalizationInfo()
            {
                SharedSecret = childSharedSecret?.GetKey() ?? Array.Empty<byte>(),
                ReturnUrl = returnUrl
            };

            return new JsonResult(finalInfo);

            //session.XToken.SharedSecretKey
            //TODO: need to send shared secret and place in local storage
            //return Redirect(returnUrl);
        }


        //

        [HttpGet(YouAuthApiPathConstants.IsAuthenticatedMethodName)]
        [Produces("application/json")]
        [Authorize(AuthenticationSchemes = YouAuthConstants.Scheme)]
        public ActionResult IsAuthenticated()
        {
            return Ok(true);
        }

        //

        [HttpGet(YouAuthApiPathConstants.DeleteTokenMethodName)]
        [Produces("application/json")]
        [Authorize(AuthenticationSchemes = YouAuthConstants.Scheme)]
        public async Task<ActionResult> DeleteToken()
        {
            if (User?.Identity?.Name != null)
            {
                await _youAuthService.DeleteSession(User.Identity.Name);
            }

            Response.Cookies.Delete(YouAuthDefaults.SessionCookieName);
            Response.Cookies.Delete(YouAuthDefaults.XTokenCookieName);
            return Ok();
        }

        //
    }
}