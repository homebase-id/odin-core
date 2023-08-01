﻿#nullable enable
#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Authorization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Mediator.Owner;
using Odin.Hosting.Controllers.OwnerToken;

namespace Odin.Hosting.Authentication.Owner
{
    /// <summary>
    /// Handles authenticating owners to their owner-console
    /// </summary>
    public class OwnerAuthenticationHandler : AuthenticationHandler<OwnerAuthenticationSchemeOptions>, IAuthenticationSignInHandler
    {
        /// <summary/>
        public OwnerAuthenticationHandler(IOptionsMonitor<OwnerAuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        /// <summary/>
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            // SEB:TODO we should split up these two into different handlers
            if (Request.Path.StartsWithSegments(OwnerApiPathConstants.YouAuthV1))
            {
                var returnUrl = WebUtility.UrlEncode(Request.GetDisplayUrl());
                // SEB:TODO constant?
                //var loginUrl = $"{Request.Scheme}://{Request.Host}/owner/login/youauth?returnUrl={returnUrl}";
                var loginUrl = $"{Request.Scheme}://{Request.Host}/owner/login?returnUrl={returnUrl}";
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

                var dotYouContext = Context.RequestServices.GetRequiredService<OdinContext>();

                if (!await UpdateOdinContext(authResult, dotYouContext))
                {
                    return AuthenticateResult.Fail("Invalid Owner Token");
                }

                if (dotYouContext.Caller.OdinId == null)
                {
                    return AuthenticateResult.Fail("Missing OdinId");
                }

                var claims = new List<Claim>()
                {
                    new Claim(ClaimTypes.Name, dotYouContext.Caller.OdinId, ClaimValueTypes.String, OdinClaimTypes.YouFoundationIssuer),
                    new Claim(OdinClaimTypes.IsAuthenticated, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer),
                    new Claim(OdinClaimTypes.IsIdentityOwner, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer),
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

        private async Task<bool> UpdateOdinContext(ClientAuthenticationToken token, OdinContext odinContext)
        {
            var authService = Context.RequestServices.GetRequiredService<OwnerAuthenticationService>();
            return await authService.UpdateOdinContext(token, odinContext);
        }

        public Task SignOutAsync(AuthenticationProperties? properties)
        {
            if (GetToken(out var result) && result != null)
            {
                var authService = Context.RequestServices.GetRequiredService<OwnerAuthenticationService>();
                authService.ExpireToken(result.Id);
            }

            return Task.CompletedTask;
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