﻿#nullable enable
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
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;

namespace Odin.Hosting.Authentication.System
{
    /// <summary>
    /// Handles authenticating calls from the system for backend processes
    /// </summary>
    public class SystemAuthenticationHandler : AuthenticationHandler<SystemAuthenticationSchemeOptions>, IAuthenticationSignInHandler
    {
        public SystemAuthenticationHandler(IOptionsMonitor<SystemAuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, ISystemClock clock, OdinConfiguration config)
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
                var config = Context.RequestServices.GetRequiredService<OdinConfiguration>();
                
                // if (token == Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e"))
                if (token == config.Host.SystemProcessApiKey)
                {
                    string domain = "system.domain";
                    var claims = new List<Claim>()
                    {
                        new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, OdinClaimTypes.YouFoundationIssuer),
                        new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, OdinClaimTypes.YouFoundationIssuer),
                        new Claim(OdinClaimTypes.IsAuthenticated, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer),
                        new Claim(OdinClaimTypes.IsSystemProcess, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer)
                    };

                    var dotYouContext = Context.RequestServices.GetRequiredService<OdinContext>();
                    dotYouContext.Caller = new CallerContext(
                        odinId: (OdinId)domain,
                        masterKey: null,
                        securityLevel: SecurityGroupType.System);

                    var permissionSet = new PermissionSet(new[] { PermissionKeys.ReadMyFollowers });
                    var sharedSecret = Guid.Empty.ToByteArray().ToSensitiveByteArray();


                    var systemPermissions = new Dictionary<string, PermissionGroup>()
                    {
                        {
                            "read_followers_only", new PermissionGroup(permissionSet, new List<DriveGrant>() { }, sharedSecret, null)
                        }
                    };

                    dotYouContext.SetPermissionContext(new PermissionContext(systemPermissions, null, true));

                    var identity = new ClaimsIdentity(claims, SystemAuthConstants.SchemeName);
                    ClaimsPrincipal principal = new ClaimsPrincipal(identity);

                    AuthenticationProperties authProperties = new AuthenticationProperties();
                    authProperties.IssuedUtc = DateTime.UtcNow;
                    authProperties.ExpiresUtc = DateTime.UtcNow.AddDays(1);
                    authProperties.AllowRefresh = true;
                    authProperties.IsPersistent = true;

                    var ticket = new AuthenticationTicket(principal, authProperties, SystemAuthConstants.SchemeName);
                    return await Task.FromResult(AuthenticateResult.Success(ticket));
                }
            }

            return await Task.FromResult(AuthenticateResult.Fail("Invalid or missing token"));
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