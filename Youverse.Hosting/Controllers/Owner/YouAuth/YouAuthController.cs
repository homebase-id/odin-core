using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Tenant;
using Youverse.Core.Util;
using Youverse.Hosting.Authentication.Owner;

#nullable enable
namespace Youverse.Hosting.Controllers.Owner.YouAuth
{
    /*
     * This controller handles the aspects of YouAuth that require
     * you to be logged in as Owner; such as creating an authorization
     * code which is used by a remote DI to validate your authentication
     * process.
     */
    [ApiController]
    [Authorize(Policy = OwnerPolicies.IsDigitalIdentityOwnerPolicyName, AuthenticationSchemes = OwnerAuthConstants.DotIdentityOwnerScheme)]
    [Route("/api/admin/youauth")]
    public class YouAuthController : Controller
    {
        private readonly IYouAuthService _youAuthService;
        private readonly string _currentTenant;

        public YouAuthController(ITenantProvider tenantProvider, IYouAuthService youAuthService)
        {
            _currentTenant = tenantProvider.GetCurrentTenant()!.Name;
            _youAuthService = youAuthService;
        }
        
        [HttpGet("create-token-flow")]
        [Produces("application/json")]
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
        
    }
}