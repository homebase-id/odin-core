using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions.Client;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Registry.Registration;
using Odin.Core.Services.Tenant;
using Odin.Core.Util;
using Odin.Hosting.Controllers.Anonymous;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;

#nullable enable

namespace Youverse.Hosting.Controllers.OwnerToken.YouAuth
{
    /*
     * This controller handles the aspects of YouAuth that require
     * you to be logged in as Owner; such as creating an authorization
     * code which is used by a remote DI to validate your authentication
     * process.
     */
    [ApiController]
    [AuthorizeValidOwnerToken]
    [Route(OwnerApiPathConstants.YouAuthV1)]
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
        public async Task<CreateTokenFlowResponse> CreateTokenFlow([FromQuery(Name = YouAuthDefaults.ReturnUrl)]string returnUrl)
        {
            if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out Uri? uri))
            {
                throw new BadRequestException(message: $"Missing or bad returnUrl '{returnUrl}'");
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
                YouAuthApiPathConstants.ValidateAuthorizationCodeRequestPath,
                queryString.ToUriComponent());

            return Redirect(redirectUrl);
        }

    }
}
