#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions.Client;
using Odin.Core.Serialization;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Tenant;
using Odin.Hosting.Authentication.ClientToken;

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
        // Authorize (GET)
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

            if (authorize.ClientType == ClientType.domain)
            {
                // If we're authorizing a domain, overwrite ClientId with domain name
                authorize.ClientId = redirectUri.Host;
            }
            else if (authorize.ClientType == ClientType.app)
            {
                // If we're authorizing an app, validate parameters in PermissionRequest
                var appParams = OdinSystemSerializer.Deserialize<YouAuthAppParameters>(authorize.PermissionRequest);
                if (appParams == null)
                {
                    throw new BadRequestException(message: $"Bad {YouAuthAuthorizeRequest.PermissionRequestName}");
                }

                // SEB:TODO validate all params
                authorize.ClientInfo = appParams.ClientFriendly;
            }

            //
            // Step [040] Logged in?
            // Authentication check and redirect to 'login' is done by controller attribute [AuthorizeValidOwnerToken]
            //

            //
            // Step [045] App registered?
            // If we're authorizing an app and it's not already registered, start that flow and return here.
            //
            if (authorize.ClientType == ClientType.app)
            {
                var appParams = OdinSystemSerializer.Deserialize<YouAuthAppParameters>(authorize.PermissionRequest);
                if (appParams == null)
                {
                    throw new BadRequestException(message: $"Bad {YouAuthAuthorizeRequest.PermissionRequestName}");
                }

                var mustRegister = await _youAuthService.AppNeedsRegistration(
                    authorize.ClientType,
                    authorize.ClientId,
                    authorize.PermissionRequest);

                if (mustRegister)
                {
                    appParams.Return = Request.GetDisplayUrl();

                    // var appRegisterPage =
                    //     $"{Request.Scheme}://{Request.Host}/owner/appreg?n=Odin%20-%20Photos&o=dev.dotyou.cloud%3A3005&appId=32f0bdbf-017f-4fc0-8004-2d4631182d1e&fn=Firefox%20%7C%20macOS&return=https%3A%2F%2Fdev.dotyou.cloud%3A3005%2Fauth%2Ffinalize%3FreturnUrl%3D%252F%26&d=%5B%7B%22a%22%3A%226483b7b1f71bd43eb6896c86148668cc%22%2C%22t%22%3A%222af68fe72fb84896f39f97c59d60813a%22%2C%22n%22%3A%22Photo%20Library%22%2C%22d%22%3A%22Place%20for%20your%20memories%22%2C%22p%22%3A3%7D%5D&pk=MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA21Hd52i8IyhMbhR9EXM0iRRI5bD7Su5MpK5WmczEEK6p%2FAAqLPPHJsreYpQHBOchd1cOTlwj4C257gRI3S2jTkI%2Fjny2u0ShzXiGr8%2BgwgmhWQYPua3QJyf4FnWFDvNO70Vw7jIe2PfSEw%2FoW718Yq1fR%2FiRasYLbzFuApwMYi%2BiD75tgIeDBnMMdgmo9JqoUq2XP5y4j4IVenVjLQqtFJezINiJQjUe2KatlofweVrYfhs3BDoJ8bdLSbGfy413QRd%2BhE4UTebi%2FQxSdAwO4Fy82%2FyKIi80qnK%2FF4qFE3q60cBTULI826cSryAulA7xOe%2B5qbyAOYh76OsICegotwIDAQAB";

                    var appRegisterPage =
                        $"{Request.Scheme}://{Request.Host}{OwnerFrontendPathConstants.AppReg}?{appParams.ToQueryString()}";

                    return Redirect(appRegisterPage);
                }
            }

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

                var consentPage =
                    $"{Request.Scheme}://{Request.Host}{OwnerFrontendPathConstants.Consent}?returnUrl={returnUrl}";
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
                authorize.CodeChallenge);

            //
            // [080] Return authorization code to client
            //
            var queryString = QueryString.Create(new Dictionary<string, string?>()
            {
                { YouAuthDefaults.Code, code },
                { YouAuthDefaults.State, authorize.State },
            });

            var uri = new UriBuilder(redirectUri)
            {
                Query = queryString.ToUriComponent()
            }.Uri;

            return Redirect(uri.ToString());
        }

        //

        //
        // Authorize (POST)
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

            // Sanity #1
            if (returnUri.Host != Request.Host.ToString())
            {
                throw new BadRequestException("Host mismatch");
            }

            // Sanity #2
            if (returnUri.AbsolutePath != Request.Path)
            {
                throw new BadRequestException("Path mismatch");
            }

            var authorize = YouAuthAuthorizeRequest.FromQueryString(returnUri.Query);
            
            authorize.Validate();
            
            await _youAuthService.StoreConsent(authorize.ClientId, authorize.PermissionRequest);

            // Redirect back to authorize
            return Redirect(returnUrl);
        }

        //

        //
        // Token (POST)
        //

        [AllowAnonymous]
        [HttpPost(OwnerApiPathConstants.YouAuthV1Token)] // "token"
        [Produces("application/json")]
        public async Task<ActionResult<YouAuthTokenResponse>> Token([FromBody] YouAuthTokenRequest tokenRequest)
        {
            tokenRequest.Validate();

            //
            // [110] Exchange auth code for access token
            // [130] Create token
            //
            var accessToken = await _youAuthService.ExchangeCodeForToken(tokenRequest.Code, tokenRequest.CodeVerifier);

            //
            // [120] Return 403 if code lookup failed
            //
            if (accessToken == null)
            {
                return Unauthorized();
            }
            
            // SEB:TODO ECC encrypt response

            //
            // [140] Return client access token to client
            //

            var result = new YouAuthTokenResponse()
            {
                Base64SharedSecret =
                    Convert.ToBase64String(accessToken.SharedSecret.GetKey())
            };
            if (tokenRequest.TokenDeliveryOption == TokenDeliveryOption.json)
            {
                result.Base64ClientAuthToken =
                    Convert.ToBase64String(accessToken.ToAuthenticationToken().ToPortableBytes());
            }
            else if (tokenRequest.TokenDeliveryOption == TokenDeliveryOption.cookie)
            {
                AuthenticationCookieUtil.SetCookie(
                    Response,
                    YouAuthDefaults.XTokenCookieName,
                    accessToken.ToAuthenticationToken());
            }

            return await Task.FromResult(result);
        }
    }
}