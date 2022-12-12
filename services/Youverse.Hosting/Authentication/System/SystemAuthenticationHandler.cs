#nullable enable
#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Authentication.System
{
    /// <summary>
    /// Handles authenticating calls from the system for backend processes
    /// </summary>
    public class SystemAuthenticationHandler : AuthenticationHandler<SystemAuthenticationSchemeOptions>, IAuthenticationSignInHandler
    {
        public SystemAuthenticationHandler(IOptionsMonitor<SystemAuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return Task.CompletedTask;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (GetToken(out var token))
            {
                //TODO: change to read from configuration or use certificate?
                //TODO: include IP address checking so this can only be called by a whitelist
                if (token == Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e"))
                {
                    string domain = "system";
                    var claims = new List<Claim>()
                    {
                        new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.IsAuthenticated, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                        new Claim(DotYouClaimTypes.IsSystemProcess, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer)
                    };

                    var dotYouContext = Context.RequestServices.GetRequiredService<DotYouContext>();
                    dotYouContext.Caller = new CallerContext(
                        dotYouId: (DotYouIdentity)domain,
                        masterKey: null,
                        securityLevel: SecurityGroupType.System);
                    
                    dotYouContext.SetPermissionContext(new PermissionContext(new Dictionary<string, PermissionGroup>(), null, false, true));

                    var identity = new ClaimsIdentity(claims, SystemAuthConstants.SchemeName);
                    ClaimsPrincipal principal = new ClaimsPrincipal(identity);

                    AuthenticationProperties authProperties = new AuthenticationProperties();
                    authProperties.IssuedUtc = DateTime.UtcNow;
                    authProperties.ExpiresUtc = DateTime.UtcNow.AddDays(1);
                    authProperties.AllowRefresh = true;
                    authProperties.IsPersistent = true;

                    var ticket = new AuthenticationTicket(principal, authProperties, SystemAuthConstants.SchemeName);
                    return AuthenticateResult.Success(ticket);
                }
            }

            return AuthenticateResult.Fail("Invalid or missing token");
        }

        public Task SignOutAsync(AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }

        public Task SignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }

        private bool GetToken(out Guid token)
        {
            //TODO: can we remove some of the sensitive cookie values from memory
            var value = Context.Request.Headers[SystemAuthConstants.Header];
            if (Guid.TryParse(value, out var result))
            {
                token = result;
                return true;
            }

            token = Guid.Empty;
            return false;
        }
    }
}