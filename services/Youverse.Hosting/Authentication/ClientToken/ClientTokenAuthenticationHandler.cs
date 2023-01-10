using System;
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
using Serilog;
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
                return await HandleAppAuth(dotYouContext);
            }

            bool isYouAuthPath = this.Context.Request.Path.StartsWithSegments(YouAuthApiPathConstants.BasePathV1, StringComparison.InvariantCultureIgnoreCase);
            if (isYouAuthPath)
            {
                return await HandleYouAuth(dotYouContext);
            }

            return AuthenticateResult.Fail("Invalid Path");
        }

        private async Task<AuthenticateResult> HandleAppAuth(DotYouContext dotYouContext)
        {
            if (!TryGetClientAuthToken(ClientTokenConstants.ClientAuthTokenCookieName, out var authToken))
            {
                AuthenticateResult.Fail("Invalid App Token");
            }

            var appRegService = Context.RequestServices.GetRequiredService<IAppRegistrationService>();
            dotYouContext.AuthContext = ClientTokenConstants.AppSchemeName;

            if (appRegService.TryGetCachedContext(authToken, out var ctx))
            {
                dotYouContext.Caller = ctx.Caller;
                dotYouContext.SetPermissionContext(ctx.PermissionsContext);
            }
            else
            {
                Log.Information("ClientTokenHandler - AppAuth: Creating new DotYouContext");
                
                var (isValid, _, _) = await appRegService.ValidateClientAuthToken(authToken);

                if (!isValid)
                {
                    AuthenticateResult.Fail("Invalid App Token");
                }

                dotYouContext.Caller = new CallerContext(
                    dotYouId: (DotYouIdentity)Request.Host.Host,
                    masterKey: null,
                    securityLevel: SecurityGroupType.Owner);

                var (appId, permissionContext) = await appRegService.GetPermissionContext(authToken);

                dotYouContext.SetPermissionContext(permissionContext);
                appRegService.CacheContext(authToken, dotYouContext);

            }

            var claims = new List<Claim>();
            claims.Add(new Claim(ClaimTypes.Name, dotYouContext.Caller.DotYouId)); //caller is this owner
            claims.Add(new Claim(DotYouClaimTypes.IsAuthorizedApp, true.ToString().ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));
            claims.Add(new Claim(DotYouClaimTypes.IsAuthenticated, true.ToString().ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));
            claims.Add(new Claim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));

            return CreateAuthenticationResult(claims, ClientTokenConstants.AppSchemeName);
        }

      
        private async Task<AuthenticateResult> HandleYouAuth(DotYouContext dotYouContext)
        {
            if (!TryGetClientAuthToken(YouAuthDefaults.XTokenCookieName, out var clientAuthToken))
            {
                return AuthenticateResult.Success(await CreateAnonYouAuthTicket(dotYouContext));
            }

            dotYouContext.AuthContext = ClientTokenConstants.YouAuthScheme;

            var youAuthRegService = this.Context.RequestServices.GetRequiredService<IYouAuthRegistrationService>();
            if (youAuthRegService.TryGetCachedContext(clientAuthToken, out var ctx))
            {
                dotYouContext.Caller = ctx.Caller;
                dotYouContext.SetPermissionContext(ctx.PermissionsContext);
            }
            else
            {
                Log.Information("ClientTokenHandler - YouAuth: Creating new DotYouContext");
                var (cc, permissionContext) = await youAuthRegService.GetPermissionContext(clientAuthToken);
                if (null == cc)
                {
                    return AuthenticateResult.Success(await CreateAnonYouAuthTicket(dotYouContext));
                }
                
                dotYouContext.Caller = cc;
                dotYouContext.SetPermissionContext(permissionContext);
             
                youAuthRegService.CacheContext(clientAuthToken, dotYouContext);
            }
            
            var claims = new List<Claim>();
            claims.Add(new Claim(ClaimTypes.Name, dotYouContext.Caller.DotYouId));
            claims.Add(new Claim(DotYouClaimTypes.IsIdentityOwner, bool.FalseString, ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));
            claims.Add(new Claim(DotYouClaimTypes.IsAuthenticated, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer));

            return CreateAuthenticationResult(claims, ClientTokenConstants.YouAuthScheme);
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
                throw new YouverseClientException("No anonymous drives configured.  There should be at least one; be sure you accessed /owner to initialize them.",
                    YouverseClientErrorCode.NotInitialized);
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
            var permissionSet = tenantContext.Settings.AnonymousVisitorsCanViewConnections
                ? new PermissionSet(new List<int>() { PermissionKeys.ReadConnections })
                : new PermissionSet();

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
                    sharedSecretKey: null
                ));

            var claims = new[]
            {
                new Claim(DotYouClaimTypes.IsIdentityOwner, bool.FalseString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                new Claim(DotYouClaimTypes.IsAuthenticated, bool.FalseString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer)
            };

            dotYouContext.AuthContext = ClientTokenConstants.YouAuthScheme;
            var claimsIdentity = new ClaimsIdentity(claims, ClientTokenConstants.YouAuthScheme);
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