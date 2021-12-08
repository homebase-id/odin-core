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
using Youverse.Hosting.Security.Authentication.Owner;

#nullable enable
namespace Youverse.Hosting.Security.Authentication
{
    /// <summary>
    /// Handles authenticating owners to their owner-console
    /// </summary>
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
                    string deviceUid = Context.Request.Headers[DotYouHeaderNames.DeviceUid].ToString();
                    if (string.IsNullOrEmpty(deviceUid))
                    {
                        deviceUid = Context.Request.Cookies[DotYouHeaderNames.DeviceUid] ?? "";
                    }
                    
                    //TODO: need to add some sort of validation that this deviceUid has not been rejected/blocked
                    
                    //TODO: this needs to be pulled from context rather than the domain
                    //TODO: need to centralize where these claims are set.  there is duplicate code in the certificate handler in Startup.cs
                    string domain = this.Context.Request.Host.Host;

                    //TODO: we need to avoid using a claim to hold the login kek.  it should just be set during the Startup.ResolveContext method
                    var loginDek = await authService.GetOwnerDek(authResult.SessionToken, authResult.ClientHalfKek);
                    var b64 = Convert.ToBase64String(loginDek.GetKey());
                    
                    var claims = new List<Claim>()
                    {
                        new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.IsIdentified, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.LoginDek, b64, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.DeviceUid, deviceUid, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.AppId, "owner-console", ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),

                        //note: the isAdminApp flag can be set true since the authService.GetOwnerDek did not throw an exception (it verifies the owner password was used)
                        new Claim(DotYouClaimTypes.IsAdminApp, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.IsIdentityOwner, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                    };

                    var identity = new ClaimsIdentity(claims, OwnerAuthConstants.DotIdentityOwnerScheme);
                    ClaimsPrincipal principal = new ClaimsPrincipal(identity);

                    AuthenticationProperties authProperties = new AuthenticationProperties();
                    authProperties.IssuedUtc = DateTime.UtcNow;
                    authProperties.ExpiresUtc = DateTime.UtcNow.AddDays(1);
                    authProperties.AllowRefresh = true;
                    authProperties.IsPersistent = true;

                    var ticket = new AuthenticationTicket(principal, authProperties, OwnerAuthConstants.DotIdentityOwnerScheme);
                    ticket.Properties.SetParameter(OwnerAuthConstants.CookieName, authResult.SessionToken);
                    return AuthenticateResult.Success(ticket);
                }
            }

            return AuthenticateResult.Fail("Invalid or missing token");
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
            var value = Context.Request.Cookies[OwnerAuthConstants.CookieName];
            if (DotYouAuthenticationResult.TryParse(value, out var result))
            {
                authResult = result;
                return true;
            }

            authResult = null;
            return false;
        }
    }
}