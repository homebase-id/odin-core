#nullable enable
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Tenant;
using Odin.Hosting.Authentication.ClientToken;
using Odin.Hosting.Controllers.Home;

namespace Odin.Hosting.Controllers.Anonymous.Home
{
    [ApiController]
    [Route(HomeApiPathConstants.AuthV1)]
    public class HomeAuthenticationController : Controller
    {
        private readonly HomeAuthenticatorService _homeAuthenticatorService;
        private readonly string _currentTenant;
        private readonly PublicPrivateKeyService _pkService;

        public HomeAuthenticationController(ITenantProvider tenantProvider, HomeAuthenticatorService homeAuthenticatorService, PublicPrivateKeyService pkService)
        {
            _currentTenant = tenantProvider.GetCurrentTenant()!.Name;
            _homeAuthenticatorService = homeAuthenticatorService;
            _pkService = pkService;
        }

        //
        // [080] Return authorization code, public key and salt to frontend.
        //
        [HttpGet(HomeApiPathConstants.HandleAuthorizationCodeCallbackMethodName)]
        public async Task<IActionResult> HandleAuthorizationCodeCallback(string code, string identity, string public_key, HomeAuthenticationState state, string salt)
        {
            try
            {
                var (fullKey, privateKey) = await _pkService.GetCurrentOfflineEccKey();
                var remotePublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(public_key);
                var exchangeSecret = fullKey.GetEcdhSharedSecret(privateKey, remotePublicKey, Convert.FromBase64String(salt));
                var exchangeSecretDigest = SHA256.Create().ComputeHash(exchangeSecret.GetKey()).ToBase64();

                //[100] Request exchange auth code for access token
                var odinId = new OdinId(identity);
                var tokenResponse = await _homeAuthenticatorService.ExchangeCodeForToken(odinId, code, exchangeSecretDigest);

                if (null == tokenResponse)
                {
                    throw new OdinClientException("failed to get token");
                }
                
                var clientAuthTokenCipher = Convert.FromBase64String(tokenResponse.Base64ClientAuthTokenCipher!);
                var clientAuthTokenIv = Convert.FromBase64String(tokenResponse.Base64ClientAuthTokenIv!);
                var clientAuthTokenBytes = AesCbc.Decrypt(clientAuthTokenCipher, ref exchangeSecret, clientAuthTokenIv);
                ClientAuthenticationToken clientAuthToken = ClientAuthenticationToken.FromPortableBytes(clientAuthTokenBytes);

                // This sharedSecret has no meaning for the home app because we don't make calls to the remote identity
              
                // var sharedSecretCipher = Convert.FromBase64String(tokenResponse.Base64SharedSecretCipher!);
                // var sharedSecretIv = Convert.FromBase64String(tokenResponse.Base64SharedSecretIv!);
                // var sharedSecret = AesCbc.Decrypt(sharedSecretCipher, ref exchangeSecret, sharedSecretIv);

                //set the cookie from the identity being logged into 
                var clientAccessToken = await _homeAuthenticatorService.RegisterBrowserAccess(odinId, clientAuthToken);
                AuthenticationCookieUtil.SetCookie(Response, YouAuthDefaults.XTokenCookieName, clientAccessToken.ToAuthenticationToken());

                //TODO: Encrypt identity and shared secret using state.EccPk64
                var sharedSecret64 = Convert.ToBase64String(clientAccessToken?.SharedSecret.GetKey() ?? Array.Empty<byte>());
                clientAccessToken?.Wipe();

                var result = OdinSystemSerializer.Serialize(new
                {
                    identity = identity,
                    ss64 = sharedSecret64
                });

                string url = $"{state.FinalUrl}?r={result}";
                return Redirect(url);
            }
            catch (OdinClientException)
            {
                string url = $"{state.FinalUrl}?error=remoteValidationCallFailed";
                Redirect(url);
            }
            catch
            {
                string url = $"{state.FinalUrl}?error=unknown";
                Redirect(url);
            }

            throw new OdinSystemException("Unhandled scenario");
        }

        //

        [HttpGet(HomeApiPathConstants.IsAuthenticatedMethodName)]
        [Produces("application/json")]
        [Authorize(AuthenticationSchemes = ClientTokenConstants.YouAuthScheme, Policy = ClientTokenPolicies.IsIdentified)]
        public ActionResult IsAuthenticated()
        {
            return Ok(true);
        }

        //

        [HttpGet(HomeApiPathConstants.DeleteTokenMethodName)]
        [Produces("application/json")]
        [Authorize(AuthenticationSchemes = ClientTokenConstants.YouAuthScheme)]
        public async Task<ActionResult> DeleteToken()
        {
            if (User?.Identity?.Name != null)
            {
                await _homeAuthenticatorService.DeleteSession(User.Identity.Name);
            }

            Response.Cookies.Delete(YouAuthDefaults.XTokenCookieName);
            return Ok();
        }

        //

        [HttpGet(HomeApiPathConstants.PingMethodName)]
        [Produces("application/json")]
        [Authorize(AuthenticationSchemes = ClientTokenConstants.YouAuthScheme, Policy = ClientTokenPolicies.IsIdentified)]
        public string GetPing([FromQuery] string text)
        {
            return $"ping from {_currentTenant}: {text}";
        }

        //
    }
}