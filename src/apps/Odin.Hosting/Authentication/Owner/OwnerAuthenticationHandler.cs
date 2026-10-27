#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Tenant;

namespace Odin.Hosting.Authentication.Owner
{
    /// <summary>
    /// Handles authenticating owners to their owner-console
    /// </summary>
    public class OwnerAuthenticationHandler : AuthenticationHandler<OwnerAuthenticationSchemeOptions>, IAuthenticationSignInHandler
    {
        private static readonly PathString[] RedirectPaths =
        [
            OwnerApiPathConstants.YouAuthV1Authorize,
            new PathString("/api/owner/v1/security/recovery/verify-email")
        ];
        
        private readonly VersionUpgradeScheduler _versionUpgradeScheduler;
        private readonly ShamirConfigurationService _shamirConfigurationService;
        private readonly ITenantProvider _tenantProvider;

        /// <summary/>
        public OwnerAuthenticationHandler(IOptionsMonitor<OwnerAuthenticationSchemeOptions> options,
            VersionUpgradeScheduler versionUpgradeScheduler,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ITenantProvider tenantProvider,
            ShamirConfigurationService shamirConfigurationService) : base(options, logger, encoder)
        {
            _versionUpgradeScheduler = versionUpgradeScheduler;
            _tenantProvider = tenantProvider;
            _shamirConfigurationService = shamirConfigurationService;
        }

        /// <summary/>
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            if (ShouldRedirect(Request.Path))
            {
                // The owner cookie is SameSite=Strict, so a browser arriving from another site (a
                // relying party sending its user to authorize, a link in an email) leaves it behind
                // even when the owner is signed in. Rather than load the owner app's login page only
                // for it to find them signed in and send them back, reload the same URL from this
                // site, which carries the cookie. The reload is same-origin, so if they really are
                // signed out it comes back here and goes on to the login page: it cannot loop.
                if (ArrivedFromAnotherSite(Request) && !Request.Cookies.ContainsKey(OwnerAuthConstants.CookieName))
                {
                    return ReloadFromThisSiteAsync();
                }

                var returnUrl = WebUtility.UrlEncode(Request.GetDisplayUrl());
                var loginUrl =
                    $"{Request.Scheme}://{Request.Host}{OwnerFrontendPathConstants.Login}?returnUrl={returnUrl}";

                Response.StatusCode = StatusCodes.Status302Found;
                Response.Redirect(loginUrl);
            }
            else
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
            }

            return Task.CompletedTask;
        }
        
        // Browsers that do not send Sec-Fetch-Site go straight to the login page, as before
        private static bool ArrivedFromAnotherSite(HttpRequest request) =>
            string.Equals(request.Headers["Sec-Fetch-Site"], "cross-site", StringComparison.OrdinalIgnoreCase);

        // A page with nothing on it but a meta refresh to the request's own URL: no script, so it
        // works under any content security policy. The link is for a browser that ignores the refresh.
        private Task ReloadFromThisSiteAsync()
        {
            var url = HtmlEncoder.Default.Encode(Request.GetDisplayUrl());

            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "text/html; charset=utf-8";
            Response.Headers.CacheControl = "no-store";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers.ContentSecurityPolicy = "default-src 'none'";

            return Response.WriteAsync(
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
                $"<meta http-equiv=\"refresh\" content=\"0;url={url}\"><title>Signing in</title></head>" +
                $"<body><a href=\"{url}\">Continue</a></body></html>");
        }

        private static bool ShouldRedirect(PathString path)
        {
            foreach (var allowed in RedirectPaths)
            {
                if (path.StartsWithSegments(allowed))
                    return true;
            }

            return false;
        }

        /// <summary/>
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (GetToken(out var authResult))
            {
                if (authResult == null)
                {
                    return AuthenticateResult.Fail("Empty authResult");
                }

                var odinContext = Context.RequestServices.GetRequiredService<IOdinContext>();

                odinContext.Tenant = (OdinId)_tenantProvider.GetCurrentTenant()?.Name;

                try
                {
                    var authService = Context.RequestServices.GetRequiredService<OwnerAuthenticationService>();
                    var pushDeviceToken = PushNotificationCookieUtil.GetDeviceKey(Context.Request);
                    var clientContext = new OdinClientContext
                    {
                        CorsHostName = null,
                        ClientIdOrDomain = null,
                        AccessRegistrationId = authResult.Id,
                        DevicePushNotificationKey = pushDeviceToken
                    };

                    if (!await authService.UpdateOdinContextAsync(authResult, clientContext, odinContext))
                    {
                        return AuthenticateResult.Fail("Invalid Owner Token");
                    }

                    await _versionUpgradeScheduler.EnsureScheduledAsync(authResult, odinContext);
                    await _shamirConfigurationService.RemoveDeadFriendDealIfNeeded(odinContext);
                }
                catch (OdinSecurityException e)
                {
                    return AuthenticateResult.Fail(e.Message);
                }

                if (odinContext.Caller.OdinId == null)
                {
                    return AuthenticateResult.Fail("Missing OdinId");
                }

                var claims = new List<Claim>()
                {
                    new Claim(ClaimTypes.Name, odinContext.Caller.OdinId, ClaimValueTypes.String, OdinClaimTypes.YouFoundationIssuer),
                    new Claim(OdinClaimTypes.IsAuthenticated, bool.TrueString.ToLower(), ClaimValueTypes.Boolean,
                        OdinClaimTypes.YouFoundationIssuer),
                    new Claim(OdinClaimTypes.IsIdentityOwner, bool.TrueString.ToLower(), ClaimValueTypes.Boolean,
                        OdinClaimTypes.YouFoundationIssuer),
                };

                var identity = new ClaimsIdentity(claims, OwnerAuthConstants.SchemeName);
                ClaimsPrincipal principal = new ClaimsPrincipal(identity);

                AuthenticationProperties authProperties = new AuthenticationProperties();
                authProperties.IssuedUtc = DateTime.UtcNow;
                authProperties.ExpiresUtc = DateTime.UtcNow.AddDays(1);
                authProperties.AllowRefresh = true;
                authProperties.IsPersistent = true;

                var ticket = new AuthenticationTicket(principal, authProperties, OwnerAuthConstants.SchemeName);
                ticket.Properties.SetParameter(OwnerAuthConstants.CookieName, authResult.Id);
                return AuthenticateResult.Success(ticket);
            }

            return AuthenticateResult.Fail("Invalid or missing token");
        }

        public async Task SignOutAsync(AuthenticationProperties? properties)
        {
            if (GetToken(out var result) && result != null)
            {
                var authService = Context.RequestServices.GetRequiredService<OwnerAuthenticationService>();
                await authService.ExpireTokenAsync(result.Id);
            }
        }

        public Task SignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }

        private bool GetToken(out ClientAuthenticationToken? authenticationResult)
        {
            //TODO: can we remove some of the sensitive cookie values from memory
            var value = Context.Request.Cookies[OwnerAuthConstants.CookieName];
            if (ClientAuthenticationToken.TryParse(value, out var result))
            {
                authenticationResult = result;
                return true;
            }

            authenticationResult = null;
            return false;
        }
    }
}