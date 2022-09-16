﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Youverse.Core;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Authentication.ClientToken
{
    public class ClientTokenAuthenticationHandler : AuthenticationHandler<ClientTokenAuthenticationSchemeOptions>
    {
        public ClientTokenAuthenticationHandler(
            IOptionsMonitor<ClientTokenAuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        //

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var dotYouContext = Context.RequestServices.GetRequiredService<DotYouContext>();

            bool isAppPath = this.Context.Request.Path.StartsWithSegments(AppApiPathConstants.BasePathV1, StringComparison.InvariantCultureIgnoreCase);
            if (isAppPath)
            {
                return await HandleAppAuth();
            }

            bool isYouAuthPath = this.Context.Request.Path.StartsWithSegments(YouAuthApiPathConstants.BasePathV1, StringComparison.InvariantCultureIgnoreCase);
            if (isYouAuthPath)
            {
                return await HandleYouAuth(dotYouContext);
            }

            return AuthenticateResult.Fail("Invalid Path");
        }

        private async Task<AuthenticateResult> HandleAppAuth()
        {
            if (!TryGetClientAuthToken(AppAuthConstants.ClientAuthTokenCookieName, out var clientAuthToken))
            {
                AuthenticateResult.Fail("Invalid App Token");
            }

            var appRegService = Context.RequestServices.GetRequiredService<IAppRegistrationService>();
            var (isValid, _, _) = await appRegService.ValidateClientAuthToken(clientAuthToken);

            if (!isValid)
            {
                AuthenticateResult.Fail("Invalid App Token");
            }

            var claims = new List<Claim>();
            claims.Add(new Claim(ClaimTypes.Name, Request.Host.Host)); //caller is this owner
            claims.Add(new Claim(DotYouClaimTypes.IsAuthorizedApp, true.ToString().ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));
            claims.Add(new Claim(DotYouClaimTypes.IsAuthenticated, true.ToString().ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));
            claims.Add(new Claim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));

            return CreateAuthenticationResult(claims, AppAuthConstants.SchemeName);
        }

        private async Task<AuthenticateResult> HandleYouAuth(DotYouContext dotYouContext)
        {
            if (!TryGetClientAuthToken(YouAuthDefaults.XTokenCookieName, out var clientAuthToken))
            {
                return AuthenticateResult.Success(await CreateAnonYouAuthTicket(dotYouContext));
            }

            var youAuthRegService = this.Context.RequestServices.GetRequiredService<IYouAuthRegistrationService>();
            var (dotYouId, isValid, isConnected, permissionContext, enabledCircleIds) = await youAuthRegService.GetPermissionContext(clientAuthToken);

            if (!isValid)
            {
                return AuthenticateResult.Success(await CreateAnonYouAuthTicket(dotYouContext));
            }

            dotYouContext.Caller = new CallerContext(
                dotYouId: dotYouId,
                securityLevel: isConnected ? SecurityGroupType.Connected : SecurityGroupType.Authenticated,
                masterKey: null,
                circleIds: enabledCircleIds
            );

            dotYouContext.SetPermissionContext(permissionContext);
            
            var claims = new List<Claim>();
            claims.Add(new Claim(ClaimTypes.Name, dotYouId));
            claims.Add(new Claim(DotYouClaimTypes.IsIdentityOwner, bool.FalseString, ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));
            claims.Add(new Claim(DotYouClaimTypes.IsAuthenticated, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));

            return CreateAuthenticationResult(claims, ClientTokenConstants.Scheme);
        }

        private AuthenticateResult CreateAuthenticationResult(IEnumerable<Claim> claims, string scheme)
        {
            var claimsIdentity = new ClaimsIdentity(claims, scheme);
            // AuthenticationProperties authProperties = new AuthenticationProperties();
            // authProperties.IssuedUtc = DateTime.UtcNow;
            // authProperties.ExpiresUtc = DateTime.UtcNow.AddDays(1);
            // authProperties.AllowRefresh = true;
            // authProperties.IsPersistent = true;

            var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), scheme);
            return AuthenticateResult.Success(ticket);
        }

        private async Task<AuthenticationTicket> CreateAnonYouAuthTicket(DotYouContext dotYouContext)
        {
            var driveService = Context.RequestServices.GetRequiredService<IDriveService>();
            var anonymousDrives = await driveService.GetAnonymousDrives(PageOptions.All);

            if (!anonymousDrives.Results.Any())
            {
                throw new YouverseException("No anonymous drives configured.  There should be at least one; be sure you accessed /owner to initialize them.");
            }

            var anonDriveGrants = anonymousDrives.Results.Select(d => new DriveGrant()
            {
                DriveId = d.Id,
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = d.TargetDriveInfo,
                    Permission = DrivePermission.Read
                }
            }).ToList();

            //HACK: granting ability to see friends list to anon users.
            var permissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadConnections });

            var permissionGroupMap = new Dictionary<string, PermissionGroup>
            {
                { "anonymous_drives", new PermissionGroup(permissionSet, anonDriveGrants, null) },
            };

            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity)string.Empty,
                securityLevel: SecurityGroupType.Anonymous,
                masterKey: null
            );

            //HACK: giving this the master key makes my hairs raise >:-[
            dotYouContext.SetPermissionContext(
                new PermissionContext(
                    permissionGroupMap,
                    sharedSecretKey: null,
                    isOwner: false
                ));

            var claims = new[]
            {
                new Claim(DotYouClaimTypes.IsIdentityOwner, bool.FalseString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                new Claim(DotYouClaimTypes.IsAuthenticated, bool.FalseString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer)
            };

            var claimsIdentity = new ClaimsIdentity(claims, ClientTokenConstants.Scheme);
            return new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), this.Scheme.Name);
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

        private bool TryGetClientAuthToken(string cookieName, out ClientAuthenticationToken clientAuthToken)
        {
            var clientAccessTokenValue64 = Context.Request.Cookies[cookieName];
            return ClientAuthenticationToken.TryParse(clientAccessTokenValue64, out clientAuthToken);
        }
    }
}