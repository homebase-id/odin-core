#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
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
using Odin.Services.Membership.Connections.IcrKeyAvailableWorker;
using Odin.Services.Tenant;
using Version = Odin.Hosting.Extensions.Version;

namespace Odin.Hosting.Authentication.Owner
{
    /// <summary>
    /// Handles authenticating owners to their owner-console
    /// </summary>
    public class OwnerAuthenticationHandler : AuthenticationHandler<OwnerAuthenticationSchemeOptions>, IAuthenticationSignInHandler
    {
        private readonly VersionUpgradeScheduler _versionUpgradeScheduler;
        private readonly IcrKeyAvailableScheduler _icrKeyAvailableScheduler;
        private readonly IcrKeyAvailableBackgroundService _icrKeyAvailableBackgroundService;
        private readonly ITenantProvider _tenantProvider;

        /// <summary/>
        public OwnerAuthenticationHandler(IOptionsMonitor<OwnerAuthenticationSchemeOptions> options,
            VersionUpgradeScheduler versionUpgradeScheduler,
            IcrKeyAvailableScheduler icrKeyAvailableScheduler,
            IcrKeyAvailableBackgroundService icrKeyAvailableBackgroundService,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ITenantProvider tenantProvider) : base(options, logger, encoder)
        {
            _versionUpgradeScheduler = versionUpgradeScheduler;
            _icrKeyAvailableScheduler = icrKeyAvailableScheduler;
            _icrKeyAvailableBackgroundService = icrKeyAvailableBackgroundService;
            _tenantProvider = tenantProvider;
        }

        /// <summary/>
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            // SEB:TODO we should split up these two into different handlers
            if (Request.Path.StartsWithSegments(OwnerApiPathConstants.YouAuthV1Authorize))
            {
                var returnUrl = WebUtility.UrlEncode(Request.GetDisplayUrl());
                var loginUrl = $"{Request.Scheme}://{Request.Host}{OwnerFrontendPathConstants.Login}?returnUrl={returnUrl}";
                Response.Redirect(loginUrl);
            }
            else
            {
                Context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            }

            return Task.CompletedTask;
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
                    await _icrKeyAvailableScheduler.EnsureScheduledAsync(authResult, odinContext, IcrKeyAvailableJobData.JobTokenType.Owner);
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