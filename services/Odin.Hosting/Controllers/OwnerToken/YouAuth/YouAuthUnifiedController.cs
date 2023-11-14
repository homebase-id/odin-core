#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Base;
using Odin.Core.Services.Membership.YouAuth;
using Odin.Core.Services.Tenant;
using Odin.Hosting.ApiExceptions.Client;
using Odin.Hosting.Extensions;

namespace Odin.Hosting.Controllers.OwnerToken.YouAuth
{
    // https://github.com/YouFoundation/stories-and-architecture-docs/blob/master/concepts/YouAuth/unified-authorization.md

    [AuthorizeValidOwnerToken]
    [Route(OwnerApiPathConstants.YouAuthV1)]
    public class YouAuthUnifiedController : Controller
    {
        private readonly ILogger<YouAuthUnifiedController> _logger;
        private readonly IYouAuthUnifiedService _youAuthService;
        private readonly string _currentTenant;

        public YouAuthUnifiedController(
            ILogger<YouAuthUnifiedController> logger,
            ITenantProvider tenantProvider,
            IYouAuthUnifiedService youAuthService)
        {
            _logger = logger;
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
                // If we're authorizing an app, overwrite ClientInfo with ClientFriendly
                var appParams = GetYouAuthAppParameters(authorize.PermissionRequest, authorize.RedirectUri);
                authorize.ClientInfo = appParams.ClientFriendly;
            }

            _logger.LogDebug("YouAuth: authorizing client_id={client_id}, redirect_uri={redirect_uri}",
                authorize.ClientType, authorize.RedirectUri);

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
                var appParams = GetYouAuthAppParameters(authorize.PermissionRequest, authorize.RedirectUri);

                var mustRegister = await _youAuthService.AppNeedsRegistration(
                    authorize.ClientId,
                    authorize.PermissionRequest);

                if (mustRegister)
                {
                    appParams.Return = Request.GetDisplayUrl();

                    var appRegisterPage =
                        $"{Request.Scheme}://{Request.Host}{OwnerFrontendPathConstants.AppReg}?{appParams.ToQueryString()}";

                    _logger.LogDebug("YouAuth: redirecting to {redirect}", appRegisterPage);
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

                _logger.LogDebug("YouAuth: redirecting to {redirect}", consentPage);
                return Redirect(consentPage);
            }

            //
            // [060] Validate scopes.
            //
            // SEB:TODO
            // Redirect to error page if something is wrong
            //

            //
            // [070]
            // Create ECC private/public key pair, random salt and shared secret based on public_key from step 30.
            // Create client access token and store it encrypted with shared secret in cache for later lookup.
            //
            var (exchangePublicKey, exchangeSalt) = await _youAuthService.CreateClientAccessToken(
                authorize.ClientType,
                authorize.ClientId,
                authorize.ClientInfo,
                authorize.PermissionRequest,
                authorize.PublicKey);

            //
            // [080] Return authorization code, public key and salt to client
            //
            var queryString = QueryString.Create(new Dictionary<string, string?>()
            {
                { YouAuthDefaults.Identity, _currentTenant },
                { YouAuthDefaults.PublicKey, exchangePublicKey },
                { YouAuthDefaults.Salt, exchangeSalt },
                { YouAuthDefaults.State, authorize.State },
            });

            var uri = new UriBuilder(redirectUri)
            {
                Query = queryString.ToUriComponent()
            }.Uri;

            _logger.LogDebug("YouAuth: redirecting to {redirect}", uri.ToString());
            return Redirect(uri.ToString());
        }

        //

        //
        // Authorize (POST)
        //

        [HttpPost(OwnerApiPathConstants.YouAuthV1Authorize)] // "authorize"
        public async Task<ActionResult> Consent(
            [FromForm(Name = YouAuthAuthorizeConsentGiven.ReturnUrlName)]
            string returnUrl,
            [FromForm(Name = YouAuthAuthorizeConsentGiven.ConsentRequirementName)]
            string consentRequirementJson)
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

            ConsentRequirements consentRequirements = ConsentRequirements.Default;
            if (!string.IsNullOrEmpty(consentRequirementJson))
            {
                var c = OdinSystemSerializer.Deserialize<ConsentRequirements>(consentRequirementJson);
                if (null != c)
                {
                    consentRequirements = c;
                }
            }

            await _youAuthService.StoreConsent(authorize.ClientId, authorize.ClientType, authorize.PermissionRequest, consentRequirements);

            // Redirect back to authorize
            _logger.LogDebug("YouAuth: redirecting to {redirect}", returnUrl);
            return Redirect(returnUrl);
        }

        //

        //
        // Token (POST)
        //

        // [100] Request exchange auth code for access token
        [AllowAnonymous]
        [HttpPost(OwnerApiPathConstants.YouAuthV1Token)] // "token"
        [Produces("application/json")]
        [EnableCors(CorsPolicies.AllowAllOriginsWithCredentialsPolicy)]
        public async Task<ActionResult<YouAuthTokenResponse>> Token([FromBody] YouAuthTokenRequest tokenRequest)
        {
            tokenRequest.Validate();

            //
            // [110] Load encrypted client access token from cache based on shared secret
            //
            var accessToken = await _youAuthService.ExchangeDigestForEncryptedToken(tokenRequest.SecretDigest);

            //
            // [120] Return 404 if code lookup failed
            //
            if (accessToken == null)
            {
                return NotFound();
            }

            var result = new YouAuthTokenResponse
            {
                Base64SharedSecretCipher = Convert.ToBase64String(accessToken.SharedSecretCipher),
                Base64SharedSecretIv = Convert.ToBase64String(accessToken.SharedSecretIv),
                Base64ClientAuthTokenCipher = Convert.ToBase64String(accessToken.ClientAuthTokenCipher),
                Base64ClientAuthTokenIv = Convert.ToBase64String(accessToken.ClientAuthTokenIv),
            };

            //
            // [140] Return client access token to client
            //
            return await Task.FromResult(result);
        }

        //

        private YouAuthAppParameters GetYouAuthAppParameters(string json, string cancelUrl)
        {
            YouAuthAppParameters appParams;

            try
            {
                appParams = OdinSystemSerializer.Deserialize<YouAuthAppParameters>(json)!;
                if (string.IsNullOrEmpty(appParams.Cancel))
                {
                    appParams.Cancel = cancelUrl;
                }
            }
            catch (Exception e)
            {
                throw new BadRequestException(message: $"Bad {YouAuthAuthorizeRequest.PermissionRequestName}", inner: e);
            }

            if (appParams == null)
            {
                throw new BadRequestException(message: $"Bad {YouAuthAuthorizeRequest.PermissionRequestName}");
            }

            appParams.Validate();

            return appParams;
        }

        //
    }
}