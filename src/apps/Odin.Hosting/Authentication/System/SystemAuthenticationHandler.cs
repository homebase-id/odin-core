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
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Authorization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;

namespace Odin.Hosting.Authentication.System
{
    /// <summary>
    /// Handles authenticating calls from the system for backend processes
    /// </summary>
    public class SystemAuthenticationHandler : AuthenticationHandler<SystemAuthenticationSchemeOptions>, IAuthenticationSignInHandler
    {
        public SystemAuthenticationHandler(IOptionsMonitor<SystemAuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, OdinConfiguration config)
            : base(options, logger, encoder)
        {
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return Task.CompletedTask;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (GetToken(out var token))
            {
                var config = Context.RequestServices.GetRequiredService<OdinConfiguration>();
                
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

                    var dotYouContext = Context.RequestServices.GetRequiredService<IOdinContext>();
                    dotYouContext.Caller = new CallerContext(
                        odinId: (OdinId)domain,
                        masterKey: null,
                        securityLevel: SecurityGroupType.System);

                    var permissionSet = new PermissionSet(new[] { PermissionKeys.ReadMyFollowers, PermissionKeys.SendPushNotifications });
                    var grantKeyStoreKey = Guid.Empty.ToByteArray().ToSensitiveByteArray();
                    
                    var systemPermissions = new Dictionary<string, PermissionGroup>()
                    {
                        {
                            "read_followers_only", new PermissionGroup(permissionSet, new List<DriveGrant>() { }, grantKeyStoreKey, null)
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
                    return Task.FromResult(AuthenticateResult.Success(ticket));
                }
            }

            return Task.FromResult(AuthenticateResult.Fail("Invalid or missing token"));
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