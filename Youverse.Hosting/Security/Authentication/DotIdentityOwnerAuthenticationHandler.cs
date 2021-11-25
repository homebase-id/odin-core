#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Web;
using Dawn;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Base;

#nullable enable
namespace Youverse.Hosting.Security.Authentication
{
    public class DotIdentityOwnerAuthenticationHandler : AuthenticationHandler<DotIdentityOwnerAuthenticationSchemeOptions>, IAuthenticationSignInHandler
    {
        public DotIdentityOwnerAuthenticationHandler(IOptionsMonitor<DotIdentityOwnerAuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            //HACK: need to review if this makes sense.  maybe instead we just host all API calls on api.frodobaggins.me.
            if (Context.Request.Path.StartsWithSegments("/api", StringComparison.InvariantCultureIgnoreCase) == false)
            {
                return Task.CompletedTask;
            }

            string returnUri = HttpUtility.UrlDecode(Context.Request.Query["return_uri"]);

            var b = new UriBuilder();
            b.Host = Context.Request.Host.Host;
            b.Scheme = Context.Request.Scheme;

            b.Query = $"return_uri={HttpUtility.UrlEncode(returnUri)}";
            b.Path = this.Options.LoginUri;

            Context.Response.Redirect(b.ToString());
            return Task.CompletedTask;
        }
        
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (GetToken(out var authResult))
            {
                var authService = Context.RequestServices.GetRequiredService<IOwnerAuthenticationService>();

                if (await authService.IsValidToken(authResult.SessionToken))
                {
                    var (appId, deviceUid, isAdminApp) = ValidateDeviceApp();
                    
                    //TODO: this needs to be pulled from context rather than the domain
                    //TODO: need to centralize where these claims are set.  there is duplicate code in the certificate handler in Startup.cs
                    string domain = this.Context.Request.Host.Host;

                    //TODO: we need to avoid using a claim to hold the login kek.  it should just be set during the Startup.ResolveContext method
                    var loginDek = await authService.GetLoginDek(authResult.SessionToken, authResult.ClientHalfKek);
                    var b64 = Convert.ToBase64String(loginDek.GetKey());

                    //HACK: todo determine how to distinguish our admin app from other apps
                    
                    var claims = new List<Claim>()
                    {
                        new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.IsIdentityOwner, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.IsIdentified, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.LoginDek, b64, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.AppId, appId, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.DeviceUid, deviceUid, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.IsAdminApp, isAdminApp.ToString().ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer)
                    };

                    var identity = new ClaimsIdentity(claims, DotYouAuthConstants.DotIdentityOwnerScheme);
                    ClaimsPrincipal principal = new ClaimsPrincipal(identity);

                    AuthenticationProperties authProperties = new AuthenticationProperties();
                    authProperties.IssuedUtc = DateTime.UtcNow;
                    authProperties.ExpiresUtc = DateTime.UtcNow.AddDays(1);
                    authProperties.AllowRefresh = true;
                    authProperties.IsPersistent = true;

                    var ticket = new AuthenticationTicket(principal, authProperties, DotYouAuthConstants.DotIdentityOwnerScheme);
                    ticket.Properties.SetParameter(DotYouAuthConstants.TokenKey, authResult.SessionToken);
                    return AuthenticateResult.Success(ticket);
                }
            }

            return AuthenticateResult.Fail("Invalid or missing token");
        }

        /// <summary>
        /// Validates the deviceUid and the appid for this request.  If valid, returns the appId and deviceUid
        /// </summary>
        /// <returns></returns>
        private (string appId, string deviceUid, bool isAdminApp) ValidateDeviceApp()
        {
            //TODO: this needs to be moved to a central location so certificate auth can use it too
            string appId = Context.Request.Headers[DotYouHeaderNames.AppId];
            string deviceUid = Context.Request.Headers[DotYouHeaderNames.DeviceUid];

            Guard.Argument(appId, nameof(appId)).NotNull().NotEmpty();
            Guard.Argument(deviceUid, nameof(deviceUid)).NotNull().NotEmpty();
            
            //TODO call to app service to validate 
            
            
            //HACK: need to determine how we ensure this is our admin app
            bool isAdminApp = false;

            //HACK: let the unit test set them selves as the admin app 
            string hack = "UNIT_TEST_IS_APP_ADMIN";
            if (base.Context.Request.Headers.ContainsKey(hack))
            {
                isAdminApp = base.Context.Request.Headers[hack] == "d43da139-fd58-FF##c-ae8d-fa252a838e09";
            }

            return (appId, deviceUid, isAdminApp);
        }

        private DotYouAuthenticationResult? GetAuthenticationResult()
        {
            DotYouAuthenticationResult result;
            var value = Context.Request.Cookies[DotYouAuthConstants.TokenKey];
            if (DotYouAuthenticationResult.TryParse(value, out result))
            {
                return result;
            }

            return null;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            return base.HandleForbiddenAsync(properties);
        }

        public Task SignOutAsync(AuthenticationProperties? properties)
        {

            if (GetToken(out var result))
            {
                var authService = Context.RequestServices.GetRequiredService<IOwnerAuthenticationService>();
                authService.ExpireToken(result.SessionToken);
            }

            return Task.CompletedTask;
        }

        public Task SignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }

        private bool GetToken(out DotYouAuthenticationResult authResult)
        {
            //the react client app uses the cookie
            string headerToken = Context.Request.Headers[DotYouAuthConstants.TokenKey];
            var value = string.IsNullOrEmpty(headerToken?.Trim()) ? Context.Request.Cookies[DotYouAuthConstants.TokenKey] : headerToken;
            if (DotYouAuthenticationResult.TryParse(value, out var result))
            {
                authResult = result;
                return true;
            }

            //TODO: need to avoid the access token on the querystring after #prototrial
            //look for token on querying string as it will come from SignalR
            // if (Context.Request.Path.StartsWithSegments("/api/live", StringComparison.OrdinalIgnoreCase) &&
            //     Context.Request.Query.TryGetValue("access_token", out var accessToken))
            // {
            //     return Guid.TryParse(accessToken, out token);
            // }
            authResult = null;
            return false;
        }
    }
}