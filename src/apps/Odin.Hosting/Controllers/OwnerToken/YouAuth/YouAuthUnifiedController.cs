#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Base;
using Odin.Services.Tenant;
using Odin.Hosting.ApiExceptions.Client;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Extensions;

namespace Odin.Hosting.Controllers.OwnerToken.YouAuth
{
    /// <summary>
    /// The identity's side of the YouAuth unified authorization flow: the <c>authorize</c> endpoint
    /// (GET, and POST for consent) and the <c>token</c> endpoint.
    /// </summary>
    /// <remarks>
    /// The <c>YouAuth [nnn]</c> comments below are the step numbers of the protocol flow diagram in
    /// <c>docs/youauth-unified-authorization.md</c>, the same spelling the reference client and the
    /// home-site login use, so a grep for a step finds every implementation of it. They read in
    /// that order: [030] the request arrives here, [040] to [055] bounce through the
    /// owner's login, app-registration and consent pages, [070] to [080] answer the relying party,
    /// and [100] to [140] are the token exchange. Steps not numbered here -- [010], [042], [047],
    /// YouAuth [090], [150], [400] -- happen in the owner's browser or at the relying party. [060] is the
    /// one out of sequence: the diagram's "show error if something is wrong" is the catch at the end
    /// of <see cref="Authorize"/>, which any earlier step can fall into.
    /// </remarks>
    [AuthorizeValidOwnerToken]
    [Route(OwnerApiPathConstants.YouAuthV1)]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class YouAuthUnifiedController : OdinControllerBase
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
            // YouAuth [030] Get authorization code
            // Validate parameters
            //
            // Two kinds of failure from here on. Until the redirect target is trusted -- the URI
            // parses, the host is the domain client's own, or the app is one this identity has
            // registered -- a bad request is answered here with a 400: nobody has vouched for the
            // redirect URI yet, and bouncing the browser to it would make this an open redirector.
            // Once it is trusted, a failure goes back to the relying party as ?error=..., the way a
            // success would have, so the relying party learns why rather than seeing its user
            // stranded on a problem-details page at someone else's domain.
            //

            if (!Uri.TryCreate(authorize.RedirectUri, UriKind.Absolute, out var redirectUri))
            {
                throw new BadRequestException(message: $"Bad {YouAuthAuthorizeRequest.RedirectUriName} '{authorize.RedirectUri}'");
            }

            var thisHost = Request.Host.Host.ToLower();

            authorize.ValidateRedirectTarget(redirectUri.Host);

            // Sanity
            if (authorize.ClientId.Equals(thisHost, StringComparison.CurrentCultureIgnoreCase))
            {
                throw new BadRequestException("Cannot YouAuth to self");
            }

            // A domain has vouched for its own host by the check above. An app vouches for its
            // redirect by being registered here, which is only known once step [045] has looked.
            // Until then a failure is a 400 at the identity; from then on it is step [060].
            var redirectTrusted = authorize.ClientType == ClientType.domain;

            try
            {
                authorize.ValidateRequest();

                _logger.LogDebug("YouAuth: authorizing client_type={client_type} client_id={client_id}, redirect_uri={redirect_uri}",
                    authorize.ClientType, authorize.ClientId, authorize.RedirectUri);

                //
                // YouAuth [040] Logged in?
                // Authentication check and redirect to 'login' is done by controller attribute [AuthorizeValidOwnerToken]
                //

                //
                // YouAuth [045] App registered?
                // If we're authorizing an app and it's not already registered, start that flow and return here.
                //
                if (authorize.ClientType == ClientType.app)
                {
                    var mustRegister = await _youAuthService.AppNeedsRegistration(
                        authorize.ClientId,
                        authorize.PermissionRequest,
                        WebOdinContext);

                    redirectTrusted = !mustRegister;

                    var appParams = GetYouAuthAppParameters(authorize.PermissionRequest, authorize.RedirectUri);

                    // If we're authorizing an app, overwrite ClientInfo with ClientFriendly
                    authorize.ClientInfo = appParams.ClientFriendly;

                    if (mustRegister)
                    {
                        appParams.Return = Request.GetDisplayUrl();

                        var appRegisterPage =
                            $"{Request.Scheme}://{thisHost}{OwnerFrontendPathConstants.AppReg}?{appParams.ToQueryString()}";

                        _logger.LogDebug("YouAuth: redirecting to {redirect}", appRegisterPage);
                        return Redirect(appRegisterPage);
                    }
                }

                //
                // YouAuth [050] Consent needed?
                //
                var needConsent = await _youAuthService.NeedConsent(
                    _currentTenant,
                    authorize.ClientType,
                    authorize.ClientId,
                    authorize.PermissionRequest,
                    authorize.RedirectUri,
                    WebOdinContext);

                if (needConsent)
                {
                    var returnUrl = WebUtility.UrlEncode(Request.GetDisplayUrl());

                    var consentPage =
                        $"{Request.Scheme}://{Request.Host}{OwnerFrontendPathConstants.Consent}?returnUrl={returnUrl}";

                    _logger.LogDebug("YouAuth: redirecting to {redirect}", consentPage);
                    return Redirect(consentPage);
                }

                //
                // YouAuth [070]
                // Create ECC private/public key pair, random salt and shared secret based on public_key from step 30.
                // Create client access token and store it encrypted with shared secret in cache for later lookup.
                //
                var (exchangePublicKey, exchangeSalt) = await _youAuthService.CreateClientAccessTokenAsync(
                    authorize.ClientType,
                    authorize.ClientId,
                    authorize.ClientInfo,
                    authorize.PermissionRequest,
                    authorize.PublicKey,
                    WebOdinContext);

                //
                // YouAuth [080] Return identity, public key, salt and state to the relying party
                //
                return RedirectToRelyingParty(redirectUri, new Dictionary<string, string?>
                {
                    { YouAuthDefaults.Identity, _currentTenant },
                    { YouAuthDefaults.PublicKey, exchangePublicKey },
                    { YouAuthDefaults.Salt, exchangeSalt },
                    { YouAuthDefaults.State, authorize.State },
                });
            }
            //
            // YouAuth [060] Show error if something is wrong.
            // Anything from [030] onward that failed lands here and goes back to the relying party,
            // provided its redirect is trusted (see the top of the method). A revoked app is by
            // definition a registered one, so its redirect is trusted whether or not step [045] got
            // as far as saying so.
            //
            catch (Exception e) when (redirectTrusted || e is OdinClientException { ErrorCode: OdinClientErrorCode.AppRevoked })
            {
                var (code, description) = e switch
                {
                    OdinClientException { ErrorCode: OdinClientErrorCode.AppRevoked } => (YouAuthDefaults.ErrorAppRevoked, e.Message),
                    OdinClientException or BadRequestException => (YouAuthDefaults.ErrorInvalidRequest, e.Message),
                    OdinSecurityException => (YouAuthDefaults.ErrorAccessDenied, null),
                    _ => ServerError(e)
                };

                return ErrorRedirect(redirectUri, authorize.State, code, description);
            }

            (string, string?) ServerError(Exception e)
            {
                // The detail belongs in the log, not on the relying party's URL.
                _logger.LogError(e, "YouAuth: authorize failed for client_type={client_type} client_id={client_id}",
                    authorize.ClientType, authorize.ClientId);
                return (YouAuthDefaults.ErrorServerError, null);
            }
        }

        //

        private ActionResult ErrorRedirect(Uri redirectUri, string state, string code, string? description)
        {
            return RedirectToRelyingParty(redirectUri, new Dictionary<string, string?>
            {
                { YouAuthDefaults.Error, code },
                { YouAuthDefaults.ErrorDescription, description },
                { YouAuthDefaults.State, state },
            });
        }

        /// <summary>
        /// Sends the browser to the relying party's redirect URI with these parameters added to
        /// whatever query it already carried. Null values are left out.
        /// </summary>
        private ActionResult RedirectToRelyingParty(Uri redirectUri, Dictionary<string, string?> parameters)
        {
            var uri = QueryHelpers.AddQueryString(redirectUri.ToString(), parameters);
            _logger.LogDebug("YouAuth: redirecting to {redirect}", uri);
            return Redirect(uri);
        }

        //

        //
        // Authorize (POST) "consent"
        //

        [HttpPost(OwnerApiPathConstants.YouAuthV1Authorize)] // "authorize"
        public async Task<ActionResult> Consent(
            [FromForm(Name = YouAuthAuthorizeConsentGiven.ReturnUrlName)]
            string returnUrl,
            [FromForm(Name = YouAuthAuthorizeConsentGiven.ConsentRequirementName)]
            string consentRequirementJson)
        {
            //
            // YouAuth [055] Give consent and redirect back
            //

            if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var returnUri))
            {
                throw new BadRequestException(message: $"Bad {YouAuthAuthorizeConsentGiven.ReturnUrlName} '{returnUrl}'");
            }

            // Sanity #1
            if (returnUri.Host != Request.Host.Host)
            {
                throw new BadRequestException("Host mismatch");
            }

            // Sanity #2
            if (returnUri.AbsolutePath != Request.Path)
            {
                throw new BadRequestException("Path mismatch");
            }

            var authorize = YouAuthAuthorizeRequest.FromQueryString(returnUri.Query);
            if (!Uri.TryCreate(authorize.RedirectUri, UriKind.Absolute, out var redirectUri))
            {
                throw new BadRequestException(message: $"Bad {YouAuthAuthorizeRequest.RedirectUriName} '{authorize.RedirectUri}'");
            }
            authorize.ValidateRedirectTarget(redirectUri.Host);
            authorize.ValidateRequest();

            // Sanity #3
            if (authorize.ClientId.Equals(Request.Host.Host, StringComparison.CurrentCultureIgnoreCase))
            {
                throw new BadRequestException("Cannot YouAuth to self");
            }

            var consentRequirements = ConsentRequirements.Default;
            if (!string.IsNullOrEmpty(consentRequirementJson))
            {
                var c = OdinSystemSerializer.Deserialize<ConsentRequirements>(consentRequirementJson);
                if (null != c)
                {
                    consentRequirements = c;
                }
            }

            
            await _youAuthService.StoreConsentAsync(authorize.ClientId, authorize.ClientType, authorize.PermissionRequest, consentRequirements, WebOdinContext);

            // Redirect back to authorize
            _logger.LogDebug("YouAuth: redirecting to {redirect}", returnUrl);
            return Redirect(returnUrl);
        }

        //

        //
        // Token (POST)
        //

        // YouAuth [100] Request exchange auth code for access token
        [AllowAnonymous]
        [HttpPost(OwnerApiPathConstants.YouAuthV1Token)] // "token"
        [Produces("application/json")]
        public async Task<ActionResult<YouAuthTokenResponse>> Token([FromBody] YouAuthTokenRequest tokenRequest)
        {
            tokenRequest.Validate();

            //
            // YouAuth [110] Load encrypted client access token from cache based on shared secret
            //
            var accessToken = await _youAuthService.ExchangeDigestForEncryptedToken(tokenRequest.SecretDigest);

            //
            // YouAuth [120] Return 404 if code lookup failed
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
            // YouAuth [140] Return client access token to client
            //
            return result;
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