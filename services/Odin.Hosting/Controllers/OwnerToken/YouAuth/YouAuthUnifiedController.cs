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
                
                string consentPage = "";
                if (authorize.ClientType == ClientType.app)
                {
                    //example: https://frodo.digital/owner/appreg?n=Odin%20-%20Photos&o=photos.odin.earth&appId=32f0bdbf-017f-4fc0-8004-2d4631182d1e&fn=Firefox%20%7C%20macOS&return=https%3A%2F%2Fphotos.odin.earth%2Fauth%2Ffinalize%3FreturnUrl%3D%252F%26&d=%5B%7B%22a%22%3A%226483b7b1f71bd43eb6896c86148668cc%22%2C%22t%22%3A%222af68fe72fb84896f39f97c59d60813a%22%2C%22n%22%3A%22Photo%20Library%22%2C%22d%22%3A%22Place%20for%20your%20memories%22%2C%22p%22%3A3%7D%5D&pk=MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA3lESpzsGk5PXQysoPZxXJ4Cp2FXycnAGxETP%2FF47EWWqDyKaR3Q1er16h4JNBZbvGQjoCgDUT5Q8vknBrnTJGL2z%2FVVdPsIenZ4IWsvI4hM%2FxQ7bQ3N4v4OJNb5f7dGtHAWrDEhpRYv1dw5s2ZnvxnxipkUc%2FUiazUuCrNV4OGTKsyeRAXdcteXrO13KK2ywl9s2eUBPLjy9OD5Vm4Du3FLDdJ2xkW6klKnINA%2BYPMFTLfeuhgJIloBMbNCyWxz0LLWiztB%2Bx0kqJyXGYPGcHxhPfUJppna6bsoJcQ462zFpkozZ%2BHROAfV324S4nHyL%2B4BvMfdcjLvEjwZAtcYy9QIDAQAB
                    
                    //TODO: the following are parameters that come in from the App
                    Guid appId = Guid.Parse("32f0bdbf-017f-4fc0-8004-2d4631182d1e"); 
                    string deviceFriendlyName = "TODO";
                    string appName = "TODO";
                    string origin = "photos.odin.earth"; //Note: this might empty if the app is something like chat

                    //TODO: Currently the client passes in a base64 public key that we use
                    //to encrypt the result; that will probably change with YouAuthUnified
                    string publicKey64 =
                        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA3lESpzsGk5PXQysoPZxXJ4Cp2FXycnAGxETP%2FF47E" +
                        "WWqDyKaR3Q1er16h4JNBZbvGQjoCgDUT5Q8vknBrnTJGL2z%2FVVdPsIenZ4IWsvI4hM%2FxQ7bQ3N4v4OJNb5f" +
                        "7dGtHAWrDEhpRYv1dw5s2ZnvxnxipkUc%2FUiazUuCrNV4OGTKsyeRAXdcteXrO13KK2ywl9s2eUBPLjy9OD5Vm4" +
                        "Du3FLDdJ2xkW6klKnINA%2BYPMFTLfeuhgJIloBMbNCyWxz0LLWiztB%2Bx0kqJyXGYPGcHxhPfUJppna6bsoJcQ" +
                        "462zFpkozZ%2BHROAfV324S4nHyL%2B4BvMfdcjLvEjwZAtcYy9QIDAQAB";

                    consentPage = $"{Request.Scheme}://{Request.Host}/owner/appreg?" +
                                  $"appId={appId}" +
                                  $"&o={origin}" +
                                  $"&n={appName}" +
                                  $"&fn={deviceFriendlyName}" +
                                  $"&return={returnUrl}";
                }

                if (authorize.ClientType == ClientType.domain)
                {
                    // SEB:TODO use path const from..?
                    // SEB:TODO clientId, clientInfo, permissionRequest?
                    consentPage = $"{Request.Scheme}://{Request.Host}/owner/youauth/authorize?returnUrl={returnUrl}";
                }

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
                out var clientAccessToken);

            //
            // [120] Return 403 if code lookup failed
            //
            if (!success)
            {
                return Unauthorized();
            }

            //
            // [140] Return client access token to client
            //
            var result = new YouAuthTokenResponse
            {
                Base64SharedSecret = sharedSecret == null ? null : Convert.ToBase64String(sharedSecret),
                Base64ClientAccessToken = clientAccessToken == null ? null : Convert.ToBase64String(clientAccessToken)
            };

            return await Task.FromResult(result);
        }
    }
}