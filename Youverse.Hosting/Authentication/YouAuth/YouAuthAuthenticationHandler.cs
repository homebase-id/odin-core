﻿using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Authorization;

namespace Youverse.Hosting.Authentication.YouAuth
{
    public class YouAuthAuthenticationHandler : AuthenticationHandler<YouAuthAuthenticationSchemeOptions>
    {
        private readonly IYouAuthSessionManager _youAuthSessionManager;

        public YouAuthAuthenticationHandler(
            IOptionsMonitor<YouAuthAuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            IYouAuthSessionManager youAuthSessionManager)
            : base(options, logger, encoder, clock)
        {
            _youAuthSessionManager = youAuthSessionManager;
        }

        //

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!TryGetSessionIdFromCookie(out Guid sessionId, out byte[] xTokenHalfKey))
            {
                return AuthenticateResult.Fail("No sessionId cookie");
            }

            var session = await _youAuthSessionManager.LoadFromId(sessionId);
            if (session == null)
            {
                return AuthenticateResult.Fail("No session matching session id");
            }
            
            var claims = new[]
            {
                new Claim(YouAuthDefaults.IdentityClaim, session.Subject),
                new Claim(DotYouClaimTypes.IsIdentityOwner, bool.FalseString, ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                new Claim(DotYouClaimTypes.IsIdentified, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer)
            };
            
            //TODO: 
            if (xTokenHalfKey.Length > 0)
            {
                //use the session xtoken to load more access
            }

            var claimsIdentity = new ClaimsIdentity(claims, nameof(YouAuthAuthenticationHandler));
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), this.Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }

        //

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Not authenticated",
                Instance = Context.Request.Path
            };
            var json = JsonSerializer.Serialize(problemDetails);

            Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            Context.Response.ContentType = "application/problem+json";
            return Context.Response.WriteAsync(json);
        }

        //

        private bool TryGetSessionIdFromCookie(out Guid sessionId, out byte[] xTokenHalfKey)
        {
            var value = Context.Request.Cookies[YouAuthDefaults.SessionCookieName] ?? "";
            xTokenHalfKey = default;
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (Guid.TryParse(value, out sessionId))
                {
                    var xtokenValue = Context.Request.Cookies[YouAuthDefaults.XTokenCookieName];
                    if (!string.IsNullOrWhiteSpace(xtokenValue))
                    {
                        xTokenHalfKey = Convert.FromBase64String(xtokenValue);
                    }

                    return true;
                }
            }

            sessionId = default;
            return false;
        }
    }
}