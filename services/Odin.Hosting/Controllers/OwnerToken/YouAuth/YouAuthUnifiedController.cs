#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
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
        private readonly ILogger<YouAuthUnifiedController> _logger;
        private readonly IYouAuthUnifiedService _youAuthService;
        private readonly YouAuthSharedSecrets _sharedSecrets;
        private readonly string _currentTenant;

        public YouAuthUnifiedController(
            ILogger<YouAuthUnifiedController> logger,
            ITenantProvider tenantProvider,
            IYouAuthUnifiedService youAuthService,
            YouAuthSharedSecrets sharedSecrets)
        {
            _logger = logger;
            _currentTenant = tenantProvider.GetCurrentTenant()!.Name;
            _youAuthService = youAuthService;
            _sharedSecrets = sharedSecrets;
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
                var appParams = GetYouAuthAppParameters(authorize.PermissionRequest);
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
                var appParams = GetYouAuthAppParameters(authorize.PermissionRequest);

                var mustRegister = await _youAuthService.AppNeedsRegistration(
                    authorize.ClientType,
                    authorize.ClientId,
                    authorize.PermissionRequest);

                if (mustRegister)
                {
                    appParams.Return = Request.GetDisplayUrl();

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
                authorize.PermissionRequest);

            //
            // [075] Create ECC key pair, random salt and shared secret.
            // SEB:TODO consider using one of identity's ECC keys instead of creating a new one
            //

            var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            var keyPair = new EccFullKeyData(privateKey, 1);
            var salt = ByteArrayUtil.GetRndByteArray(16);

            var remotePublicKey = EccPublicKeyData.FromDerEncodedPublicKey(Convert.FromBase64String(authorize.PublicKey));
            var sharedSecret = keyPair.GetEcdhSharedSecret(privateKey, remotePublicKey, salt);
            var sharedSecretDigest = SHA256.Create().ComputeHash(sharedSecret.GetKey()).ToBase64();

            _sharedSecrets.SetSecret(sharedSecretDigest, sharedSecret);

            //
            // [080] Return authorization code, public key and salt to client
            //
            var queryString = QueryString.Create(new Dictionary<string, string?>()
            {
                { YouAuthDefaults.Code, code },
                { YouAuthDefaults.Identity, _currentTenant },
                { YouAuthDefaults.PublicKey, keyPair.publicDerBase64() },
                { YouAuthDefaults.Salt, Convert.ToBase64String(salt) },
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
            this.Response.Headers.Add("Access-Control-Allow-Origin", (string)this.Request.Headers["Origin"]);
            this.Response.Headers.Add("Access-Control-Allow-Credentials", "true");

            tokenRequest.Validate();

            //
            // [105] Look up ECC keypair from secret digest
            //
            if (!_sharedSecrets.TryGetSecret(tokenRequest.SecretDigest, out SensitiveByteArray exchangeSecret))
            {
                throw new BadRequestException($"Invalid digest {tokenRequest.SecretDigest}");
            }

            //
            // [110] Exchange auth code for access token
            // [130] Create token
            //
            var accessToken = await _youAuthService.ExchangeCodeForToken(tokenRequest.Code);

            //
            // [120] Return 404 if code lookup failed
            //
            if (accessToken == null)
            {
                return NotFound();
            }

            //
            // [140] Return client access token to client
            //

            var sharedSecretPlain = accessToken.SharedSecret.GetKey();
            var (sharedSecretIv, sharedSecretCipher) = AesCbc.Encrypt(sharedSecretPlain, ref exchangeSecret);
            var result = new YouAuthTokenResponse
            {
                Base64SharedSecretCipher = Convert.ToBase64String(sharedSecretCipher),
                Base64SharedSecretIv = Convert.ToBase64String(sharedSecretIv),
            };

            if (tokenRequest.TokenDeliveryOption == TokenDeliveryOption.json)
            {
                var (clientAuthTokenIv, clientAuthTokenCipher) =
                    AesCbc.Encrypt(accessToken.ToAuthenticationToken().ToPortableBytes(), ref exchangeSecret);

                result.Base64ClientAuthTokenCipher = Convert.ToBase64String(clientAuthTokenCipher);
                result.Base64ClientAuthTokenIv = Convert.ToBase64String(clientAuthTokenIv);
            }
            else // (tokenRequest.TokenDeliveryOption == TokenDeliveryOption.cookie)
            {
                AuthenticationCookieUtil.SetCookie(
                    Response,
                    YouAuthDefaults.XTokenCookieName,
                    accessToken.ToAuthenticationToken());
            }

            return await Task.FromResult(result);
        }

        //

        private YouAuthAppParameters GetYouAuthAppParameters(string json)
        {
            YouAuthAppParameters appParams;

            try
            {
                appParams = OdinSystemSerializer.Deserialize<YouAuthAppParameters>(json)!;
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
