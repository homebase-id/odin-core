#nullable enable
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Tenant;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Home.Service;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;

namespace Odin.Hosting.Controllers.Home.Auth
{
    [ApiController]
    [Route(HomeApiPathConstants.AuthV1)]
    public class HomeAuthenticationController : OdinControllerBase
    {
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;
        private readonly HomeAuthenticatorService _homeAuthenticatorService;
        private readonly string _currentTenant;
        private readonly PublicPrivateKeyService _pkService;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public HomeAuthenticationController(ITenantProvider tenantProvider, HomeAuthenticatorService homeAuthenticatorService,
            PublicPrivateKeyService pkService, IOdinHttpClientFactory odinHttpClientFactory, TenantSystemStorage tenantSystemStorage)
        {
            _currentTenant = tenantProvider.GetCurrentTenant()!.Name;
            _homeAuthenticatorService = homeAuthenticatorService;
            _pkService = pkService;
            _odinHttpClientFactory = odinHttpClientFactory;
            _tenantSystemStorage = tenantSystemStorage;
        }

        //
        // [080] Return authorization code, public key and salt to frontend.
        //
        [HttpGet(HomeApiPathConstants.HandleAuthorizationCodeCallbackMethodName)]
        public async Task<IActionResult> HandleAuthorizationCodeCallback(string identity, string public_key, [FromQuery] string state,
            string salt)
        {
            var authState = OdinSystemSerializer.Deserialize<HomeAuthenticationState>(HttpUtility.UrlDecode(state));

            if (string.IsNullOrEmpty(authState?.FinalUrl))
            {
                throw new OdinClientException("Invalid state");
            }

            try
            {
                var db = _tenantSystemStorage.IdentityDatabase;
                var (fullKey, privateKey) = await _pkService.GetCurrentOfflineEccKeyAsync(db);
                var remotePublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(public_key);
                var exchangeSecret = fullKey.GetEcdhSharedSecret(privateKey, remotePublicKey, Convert.FromBase64String(salt));
                var exchangeSecretDigest = SHA256.Create().ComputeHash(exchangeSecret.GetKey()).ToBase64();

                //[100] Request exchange auth code for access token
                var odinId = new OdinId(identity);
                var tokenResponse = await this.ExchangeDigestForToken(odinId, exchangeSecretDigest);

                if (null == tokenResponse)
                {
                    throw new OdinClientException("failed to get token");
                }

                var clientAuthTokenCipher = Convert.FromBase64String(tokenResponse.Base64ClientAuthTokenCipher!);
                var clientAuthTokenIv = Convert.FromBase64String(tokenResponse.Base64ClientAuthTokenIv!);
                var clientAuthTokenBytes = AesCbc.Decrypt(clientAuthTokenCipher, exchangeSecret, clientAuthTokenIv);
                ClientAuthenticationToken clientAuthToken = ClientAuthenticationToken.FromPortableBytes(clientAuthTokenBytes);

                // This sharedSecret has no meaning for the home app because we don't make calls to the remote identity

                // var sharedSecretCipher = Convert.FromBase64String(tokenResponse.Base64SharedSecretCipher!);
                // var sharedSecretIv = Convert.FromBase64String(tokenResponse.Base64SharedSecretIv!);
                // var sharedSecret = AesCbc.Decrypt(sharedSecretCipher, ref exchangeSecret, sharedSecretIv);

                //set the cookie from the identity being logged into

                var clientAccessToken = await _homeAuthenticatorService.RegisterBrowserAccess(odinId, clientAuthToken, db);
                AuthenticationCookieUtil.SetCookie(Response, YouAuthDefaults.XTokenCookieName, clientAccessToken!.ToAuthenticationToken());

                var url = GetFinalUrl(odinId, clientAccessToken, authState);
                return Redirect(url);
            }
            catch (OdinClientException)
            {
                string url = $"{authState.FinalUrl}?error=remoteValidationCallFailed";
                return Redirect(url);
            }
            catch
            {
                string url = $"{authState.FinalUrl}?error=unknown";
                return Redirect(url);
            }
        }

        /// <summary>
        /// Encrypts the final results using ECC for the home-app
        /// </summary>
        private string GetFinalUrl(OdinId odinId, ClientAccessToken clientAccessToken, HomeAuthenticationState authState)
        {
            var homeClientPublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(authState.EccPk64);
            var salt = ByteArrayUtil.GetRndByteArray(16);
            var keyPairPassword = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            EccFullKeyData transferKeyPair = new EccFullKeyData(keyPairPassword, EccKeySize.P384, 2);
            var clientTransferSharedSecret = transferKeyPair.GetEcdhSharedSecret(keyPairPassword, homeClientPublicKey, salt);

            var catSharedSecret64 = Convert.ToBase64String(clientAccessToken?.SharedSecret.GetKey() ?? Array.Empty<byte>());
            clientAccessToken?.Wipe();

            var sensitivePayload = OdinSystemSerializer.Serialize(new
            {
                identity = odinId,
                ss64 = catSharedSecret64,
                returnUrl = authState.ReturnUrl
            }).ToUtf8ByteArray();

            var (randomIv, cipher) = AesCbc.Encrypt(sensitivePayload, clientTransferSharedSecret);

            var eccInfo = OdinSystemSerializer.Serialize(new
            {
                pk = transferKeyPair.PublicKeyJwkBase64Url(),
                salt = salt,
                iv = randomIv
            });

            string url = $"{authState.FinalUrl}?r={cipher.ToBase64()}&ecc={eccInfo}";
            return url;
        }

        //

        [HttpGet(HomeApiPathConstants.IsAuthenticatedMethodName)]
        [Produces("application/json")]
        [Authorize(AuthenticationSchemes = YouAuthConstants.YouAuthScheme, Policy = YouAuthPolicies.IsIdentified)]
        public ActionResult IsAuthenticated()
        {
            return Ok(true);
        }

        //

        [HttpGet(HomeApiPathConstants.DeleteTokenMethodName)]
        [Produces("application/json")]
        public async Task<ActionResult> DeleteToken()
        {
            Response.Cookies.Delete(YouAuthDefaults.XTokenCookieName);
            var db = _tenantSystemStorage.IdentityDatabase;
            await _homeAuthenticatorService.DeleteSessionAsync(WebOdinContext, db);

            return Ok();
        }

        //

        [HttpGet(HomeApiPathConstants.PingMethodName)]
        [Produces("application/json")]
        [Authorize(AuthenticationSchemes = YouAuthConstants.YouAuthScheme, Policy = YouAuthPolicies.IsIdentified)]
        public string GetPing([FromQuery] string text)
        {
            return $"ping from {_currentTenant}: {text}";
        }

        //

        private async Task<YouAuthTokenResponse?> ExchangeDigestForToken(OdinId odinId, string digest)
        {
            var tokenRequest = new YouAuthTokenRequest
            {
                SecretDigest = digest
            };

            var response = await _odinHttpClientFactory
                .CreateClient<IHomePerimeterHttpClient>(odinId)
                .ExchangeCodeForToken(tokenRequest);

            if (response.IsSuccessStatusCode && response.Content != null)
            {
                return response.Content;
            }

            return null;

            //TODO: need to determine how to handle these scenarios

            // if (response.StatusCode == HttpStatusCode.BadRequest)
            // {
            // }
            //
            // if (response.StatusCode == HttpStatusCode.NotFound)
            // {
            //     throw new OdinClientException("");
            // }

            // throw new OdinSystemException("unhandled scenario");
        }
    }
}