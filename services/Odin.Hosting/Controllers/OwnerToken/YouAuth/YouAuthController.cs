#nullable enable

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

namespace Odin.Hosting.Controllers.OwnerToken.YouAuth
{
    /*
     * This controller handles the aspects of YouAuth that require
     * you to be logged in as Owner; such as creating an authorization
     * code which is used by a remote DI to validate your authentication
     * process.
     */



    /*
     PKCE: Proof Key for Code Exchange
     code verifier: string[43..128]
     code challenge: hash of code verifier
     
     A) Sam logs on to frodo.me/home with his identity
       1) Sam authenticates at sam.me/owner
       YOUAUTH BEGIN
       2) Sam consents to frodo.me's requested scopes (identity:read)
       3) Sam's browser gets an auth code from sam.me/owner
       4) Sam's browser GET frodo.me/youauth/token?code=... 
         4.1) back channel: frodo.me exchanges auth code for access token with sam.me
         4.2) frodo.me returns access token to Sam's browser
       YOUAUTH END         
       5) Sam's browser POST frodo.me/youauth/cookie?token=... 
         5.1) frodo.me creates a cookie representation of the access token.
         
     B) Sam logs on to photos.odin.earth with his identity
       1) Sam authenticates at sam.me/owner
       YOUAUTH BEGIN
       2) Sam consents to photo-app's requested scopes (uuid-of-drive:read uuid-of-drive:write)
       3) Sam's browser gets an auth code from sam.me/owner
       ..
       ..
       ..         
         
     C) Sam logs on to amazon.com with his identity
       1) Sam authenticates at sam.me/owner
       YOUAUTH BEGIN
       2) Sam consents to amazon.com's requested scopes (email:read)
       3) Sam's browser gets an auth code from sam.me/owner
       ..
       ..
       ..         

                
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
        private readonly IYouAuthService _youAuthService;
        private readonly string _currentTenant;

        public YouAuthController(ITenantProvider tenantProvider, IYouAuthService youAuthService)
        {
            _currentTenant = tenantProvider.GetCurrentTenant()!.Name;
            _youAuthService = youAuthService;
        }

        [HttpGet("create-token-flow")]
        [Produces("application/json")]
        [Obsolete("SEB:TODO delete this method")]
        public async Task<ActionResult> CreateTokenFlow([FromQuery(Name = YouAuthDefaults.ReturnUrl)] string returnUrl)
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

            var redirectUrl = $"https://{DnsConfigurationSet.PrefixApi}.{initiator}".UrlAppend(
                YouAuthApiPathConstants.ValidateAuthorizationCodeRequestPath,
                queryString.ToUriComponent());

            return Redirect(redirectUrl);
        }

    }

    //
    // Authorize
    // OAUTH2 equivalent: https://learn.microsoft.com/en-us/azure/active-directory/develop/v2-oauth2-auth-code-flow#request-an-authorization-code
    //

    [HttpGet(YouAuthApiPathConstants.RequestAuthorizationCodeMethodName)] // "authorize"
    [Produces("application/json")]
    public async Task<ActionResult> Authorize([FromQuery(Name = YouAuthDefaults.RedirectUri)] string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out Uri? uri))
        {
            throw new BadRequestException(message: $"Missing or bad {YouAuthDefaults.RedirectUri} '{redirectUri}'");
        }

        //
        // A new static json page "app-info.json" must be deployed with every app (web AND native)
        // It will contain data like
        //  app uuid
        //  app name
        //  app description
        //  supported scopes (name and description)
        //  signature/seal of approval/warm and fuzzy secure feelings
        //  etc. etc. 
        //

        // client_id: app info url (see above)
        //            

        // scope: list of scopes to consent to e.g. identity:read uuid-of-drive:read uuid-of-drive:write 
        //        (note: drive is created by owner if it doesnt already exists)

        // code_challenge: (see above code challenge)
        //

        // code_challenge_method: S256 (sha 256)
        //

        // domain_hint:  

        //
        // CONSENT:
        // Application {app.info.name} is requesting the following permissions to your data:
        //   Read {app.info.scopes[identity:read]}
        //   Write {app.info.scopes[uuid-of-drive:write]}
        //
        // Yes/no ?
        //


        var initiator = uri.Host;
        var subject = _currentTenant;

        var authorizationCode = await _youAuthService.CreateAuthorizationCode(initiator, subject);

        var queryString = QueryString.Create(new Dictionary<string, string?>()
        {
            {YouAuthDefaults.AuthorizationCode, authorizationCode},
            {YouAuthDefaults.Subject, subject},
            {YouAuthDefaults.ReturnUrl, redirectUri},
        });

        var redirectUrl = $"https://{DnsConfigurationSet.PrefixApi}.{initiator}".UrlAppend(
            YouAuthApiPathConstants.ValidateAuthorizationCodeRequestPath,
            queryString.ToUriComponent());

        return Redirect(redirectUrl);
    }

    //

}

