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
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.YouAuth;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Controllers.Home.Service;
using Odin.Services.Peer.AppNotification;

namespace Odin.Hosting.Authentication.YouAuth
{
    public class YouAuthAuthenticationHandler(
        IOptionsMonitor<YouAuthAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<YouAuthAuthenticationSchemeOptions>(options, logger, encoder)
    {
        //

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var dotYouContext = Context.RequestServices.GetRequiredService<IOdinContext>();

            bool isAppPath =
                this.Context.Request.Path.StartsWithSegments(AppApiPathConstants.BasePathV1, StringComparison.InvariantCultureIgnoreCase);
            if (isAppPath)
            {
                return await HandleAppAuth(dotYouContext);
            }

            bool isYouAuthPath =
                this.Context.Request.Path.StartsWithSegments(GuestApiPathConstants.BasePathV1, StringComparison.InvariantCultureIgnoreCase);
            if (isYouAuthPath)
            {
                return await HandleYouAuth(dotYouContext);
            }

            return AuthenticateResult.Fail("Invalid Path");
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

        private async Task<AuthenticateResult> HandleAppAuth(IOdinContext odinContext)
        {
            if (!TryGetClientAuthToken(YouAuthConstants.AppCookieName, out var authToken, true))
            {
                return AuthenticateResult.Fail("Invalid App Token");
            }

            var appRegService = Context.RequestServices.GetRequiredService<IAppRegistrationService>();
            odinContext.SetAuthContext(YouAuthConstants.AppSchemeName);

            var ctx = await appRegService.GetAppPermissionContextAsync(authToken, odinContext);

            if (null == ctx)
            {
                return AuthenticateResult.Fail("Invalid App Token");
            }

            odinContext.Caller = ctx.Caller;
            odinContext.SetPermissionContext(ctx.PermissionsContext);

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, odinContext.GetCallerOdinIdOrFail()), //caller is this owner
                new(OdinClaimTypes.IsAuthorizedApp, true.ToString().ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer),
                new(OdinClaimTypes.IsAuthenticated, true.ToString().ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer),
                new(OdinClaimTypes.IsIdentityOwner, true.ToString().ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer)
            };

            // Steal this path from the http controller because here we have the client auth token
            if (Context.Request.Path.StartsWithSegments($"{AppApiPathConstants.NotificationsV1}/preauth"))
            {
                AuthenticationCookieUtil.SetCookie(Response, YouAuthConstants.AppCookieName, authToken);
            }

            return CreateAuthenticationResult(claims, YouAuthConstants.AppSchemeName);
        }


        private async Task<AuthenticateResult> HandleYouAuth(IOdinContext odinContext)
        {
            /////
            odinContext.SetAuthContext(YouAuthConstants.YouAuthScheme);

            if (!TryGetClientAuthToken(YouAuthDefaults.XTokenCookieName, out var clientAuthToken))
            {
                return AuthenticateResult.Success(await CreateAnonYouAuthTicketAsync(odinContext));
            }

            if (clientAuthToken.ClientTokenType == ClientTokenType.BuiltInBrowserApp)
            {
                return await HandleBuiltInBrowserAppToken(clientAuthToken, odinContext);
            }

            if (clientAuthToken.ClientTokenType == ClientTokenType.YouAuth)
            {
                return await HandleYouAuthTokenAsync(clientAuthToken, odinContext);
            }

            throw new OdinClientException("Unhandled youauth token type");
        }

        private async Task<AuthenticateResult> HandleBuiltInBrowserAppToken(ClientAuthenticationToken clientAuthToken,
            IOdinContext odinContext)
        {
            if (Request.Query.TryGetValue(GuestApiQueryConstants.IgnoreAuthCookie, out var values))
            {
                if (Boolean.TryParse(values.FirstOrDefault(), out var shouldIgnoreAuth))
                {
                    if (shouldIgnoreAuth)
                    {
                        return AuthenticateResult.Success(await CreateAnonYouAuthTicketAsync(odinContext));
                    }
                }
            }

            var homeAuthenticatorService = this.Context.RequestServices.GetRequiredService<HomeAuthenticatorService>();
            var ctx = await homeAuthenticatorService.GetDotYouContextAsync(clientAuthToken, odinContext);

            if (null == ctx)
            {
                //if still no context, fall back to anonymous
                return AuthenticateResult.Success(await CreateAnonYouAuthTicketAsync(odinContext));
            }

            odinContext.Caller = ctx.Caller;
            odinContext.SetPermissionContext(ctx.PermissionsContext);
            return CreateAuthenticationResult(GetYouAuthClaims(odinContext), YouAuthConstants.YouAuthScheme);
        }

        private async Task<AuthenticateResult> HandleYouAuthTokenAsync(ClientAuthenticationToken clientAuthToken, IOdinContext odinContext)
        {
            var youAuthRegService = this.Context.RequestServices.GetRequiredService<YouAuthDomainRegistrationService>();
            var ctx = await youAuthRegService.GetDotYouContextAsync(clientAuthToken, odinContext);
            if (null == ctx)
            {
                //if still no context, fall back to anonymous
                return AuthenticateResult.Success(await CreateAnonYouAuthTicketAsync(odinContext));
            }

            odinContext.Caller = ctx.Caller;
            odinContext.SetPermissionContext(ctx.PermissionsContext);

            return CreateAuthenticationResult(GetYouAuthClaims(odinContext), YouAuthConstants.YouAuthScheme);
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

        private async Task<AuthenticationTicket> CreateAnonYouAuthTicketAsync(IOdinContext odinContext)
        {
            var driveManager = Context.RequestServices.GetRequiredService<IDriveManager>();
            var anonymousDrives = await driveManager.GetAnonymousDrivesAsync(PageOptions.All, odinContext);

            if (!anonymousDrives.Results.Any())
            {
                throw new OdinClientException(
                    "No anonymous drives configured.  There should be at least one; be sure you accessed /owner to initialize them.",
                    OdinClientErrorCode.NotInitialized);
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

            var tenantContext = this.Context.RequestServices.GetRequiredService<TenantContext>();
            var anonPerms = new List<int>();

            if (tenantContext.Settings.AnonymousVisitorsCanViewConnections)
            {
                anonPerms.Add(PermissionKeys.ReadConnections);
            }

            if (tenantContext.Settings.AnonymousVisitorsCanViewWhoIFollow)
            {
                anonPerms.Add(PermissionKeys.ReadWhoIFollow);
            }

            var permissionGroupMap = new Dictionary<string, PermissionGroup>
            {
                { "read_anonymous_drives", new PermissionGroup(new PermissionSet(anonPerms), anonDriveGrants, null, null) },
            };

            odinContext.Caller = new CallerContext(
                odinId: null,
                securityLevel: SecurityGroupType.Anonymous,
                masterKey: null
            );

            odinContext.SetPermissionContext(
                new PermissionContext(
                    permissionGroupMap,
                    sharedSecretKey: null
                ));

            var claims = new[]
            {
                new Claim(OdinClaimTypes.IsIdentityOwner, bool.FalseString.ToLower(), ClaimValueTypes.Boolean,
                    OdinClaimTypes.YouFoundationIssuer),
                new Claim(OdinClaimTypes.IsAuthenticated, bool.FalseString.ToLower(), ClaimValueTypes.Boolean,
                    OdinClaimTypes.YouFoundationIssuer)
            };

            var claimsIdentity = new ClaimsIdentity(claims, YouAuthConstants.YouAuthScheme);
            return new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), this.Scheme.Name);
        }

        private List<Claim> GetYouAuthClaims(IOdinContext odinContext)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, odinContext.GetCallerOdinIdOrFail()),
                new(OdinClaimTypes.IsIdentityOwner, bool.FalseString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer),
                new(OdinClaimTypes.IsAuthenticated, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer)
            };
            return claims;
        }

        private bool TryGetClientAuthToken(string cookieName, out ClientAuthenticationToken clientAuthToken, bool preferHeader = false)
        {
            var clientAuthToken64 = string.Empty;
            if (preferHeader)
            {
                clientAuthToken64 = Context.Request.Headers[cookieName];
            }

            if (string.IsNullOrWhiteSpace(clientAuthToken64))
            {
                clientAuthToken64 = Context.Request.Cookies[cookieName];
            }

            return ClientAuthenticationToken.TryParse(clientAuthToken64, out clientAuthToken);
        }
    }
}
