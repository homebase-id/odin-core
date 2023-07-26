#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions.Client;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Tenant;

namespace Odin.Hosting.Controllers.OwnerToken.YouAuth
{
    // https://github.com/YouFoundation/stories-and-architecture-docs/blob/master/concepts/YouAuth/unified-authorization.md

    [AuthorizeValidOwnerToken]
    [Route(OwnerApiPathConstants.YouAuthV1)]
    public class YouAuthUnifiedController : Controller
    {
        private readonly IYouAuthUnifiedService _youAuthService;
        private readonly string _currentTenant;

        public YouAuthUnifiedController(ITenantProvider tenantProvider, IYouAuthUnifiedService youAuthService)
        {
            _currentTenant = tenantProvider.GetCurrentTenant()!.Name;
            _youAuthService = youAuthService;
        }

        //
        // Authorize
        //
        // OAUTH2 equivalent: https://learn.microsoft.com/en-us/azure/active-directory/develop/v2-oauth2-auth-code-flow#request-an-authorization-code
        //

        [HttpGet(OwnerApiPathConstants.YouAuthV1Authorize)] // "authorize"
        public async Task<ActionResult> Authorize([FromQuery] YouAuthAuthorizeRequest authorize)
        {
            //
            // Step [030] Get authorization code
            // Validate parameters
            //
            authorize.Validate();
            if (!Uri.TryCreate(authorize.RedirectUri, UriKind.Absolute, out var redirectUri))
            {
                throw new BadRequestException(message: $"Bad {YouAuthAuthorizeRequest.RedirectUriName} '{authorize.RedirectUri}'");
            }

            // If we're authorizing a domain, overwrite ClientId with domain name
            if (authorize.ClientType == ClientType.domain)
            {
                authorize.ClientId = redirectUri.Host;
            }

            //
            // Step [040] Logged in?
            // Authentication check and redirect to 'login' is done by controller attribute [AuthorizeValidOwnerToken]
            //

            //
            // Step [050] Consent needed?
            //
            var needConsent = await _youAuthService.NeedConsent(
                _currentTenant,
                authorize.ClientType,
                authorize.ClientId,
                authorize.PermissionRequest);

            if (needConsent)
            {
                var returnUrl = WebUtility.UrlEncode(Request.GetDisplayUrl());
                
                // SEB:TODO use path const from..?
                // SEB:TODO clientId, clientInfo, permissionRequest?
                var consentPage = $"{Request.Scheme}://{Request.Host}/owner/consent?returnUrl={returnUrl}";
                
                return Redirect(consentPage);
            }

            //
            // [060] Validate scopes.
            //
            // SEB:TODO
            // Redirect to error page if something is wrong
            //

            //
            // [070] Create authorization code
            //
            var code = await _youAuthService.CreateAuthorizationCode(
                authorize.ClientType,
                authorize.ClientId,
                authorize.ClientInfo,
                authorize.PermissionRequest,
                authorize.CodeChallenge,
                authorize.TokenDeliveryOption);

            //
            // [080] Return authorization code to client
            //
            var queryString = QueryString.Create(new Dictionary<string, string?>()
            {
                { YouAuthDefaults.Code, code },
            });

            var uri = new UriBuilder(redirectUri)
            {
                Query = queryString.ToUriComponent()
            }.Uri;

            return Redirect(uri.ToString());
        }

        //

        [HttpPost(OwnerApiPathConstants.YouAuthV1Authorize)] // "authorize"
        public async Task<ActionResult> Consent(
            [FromForm(Name = YouAuthAuthorizeConsentGiven.ReturnUrlName)]
            string returnUrl)
        {
            // SEB:TODO CSRF ValidateAntiForgeryToken

            //
            // [055] Give consent and redirect back
            //
            if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var returnUri))
            {
                throw new BadRequestException(message: $"Bad {YouAuthAuthorizeConsentGiven.ReturnUrlName} '{returnUrl}'");
            }

            // Sanity 
            if (returnUri.Host != Request.Host.ToString())
            {
                throw new BadRequestException("Host mismatch");
            }

            var authorize = YouAuthAuthorizeRequest.FromQueryString(returnUri.Query);
            authorize.Validate();

            await _youAuthService.StoreConsent(authorize.ClientId, authorize.PermissionRequest);

            return Redirect(returnUrl);
        }

        //

        [HttpPost(OwnerApiPathConstants.YouAuthV1Token)] // "token"
        [Produces("application/json")]
        public async Task<ActionResult<YouAuthTokenResponse>> Token([FromBody] YouAuthTokenRequest tokenRequest)
        {
            //
            // [110] Exchange auth code for access token
            // [130] Create token
            //
            var success = await _youAuthService.ExchangeCodeForToken(
                tokenRequest.Code,
                tokenRequest.CodeVerifier,
                out var sharedSecret,
                out var clientAuthToken);

            //
            // [120] Return 403 if code lookup failed
            //
            if (!success)
            {
                return Unauthorized();
            }
            
            //SEB TODO: ECC encrypt response

            //
            // [140] Return client access token to client
            //
            var result = new YouAuthTokenResponse
            {
                Base64SharedSecret = sharedSecret == null ? null : Convert.ToBase64String(sharedSecret),
                Base64ClientAccessToken = clientAuthToken == null ? null : Convert.ToBase64String(clientAuthToken)
            };

            return await Task.FromResult(result);
        }
    }
}