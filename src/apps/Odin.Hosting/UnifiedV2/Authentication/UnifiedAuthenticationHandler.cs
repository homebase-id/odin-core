#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.UnifiedV2.Authentication.Handlers;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Hosting.UnifiedV2.Authentication
{
    public class UnifiedAuthenticationHandler
        : AuthenticationHandler<UnifiedAuthenticationSchemeOptions>, IAuthenticationSignInHandler
    {
        private readonly OdinConfiguration _config;
        private readonly ILogger<UnifiedAuthenticationHandler> _localLogger;

        /// <summary/>
        public UnifiedAuthenticationHandler(IOptionsMonitor<UnifiedAuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, OdinConfiguration config, ILogger<UnifiedAuthenticationHandler> localLogger)
            : base(options, logger, encoder)
        {
            _config = config;
            _localLogger = localLogger;
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
            var odinContext = Context.RequestServices.GetRequiredService<IOdinContext>();

            if (!TryFindClientAuthToken(out var token))
            {
                return await CreateAnonResult(Context, odinContext);
            }

            try
            {
                switch (token.ClientTokenType)
                {
                    case ClientTokenType.Cdn:
                        return await CdnAuthPathHandler.Handle(Context, token, odinContext);

                    case ClientTokenType.Owner:
                        return await OwnerAuthPathHandler.Handle(Context, token, odinContext);

                    case ClientTokenType.App:
                        return await AppAuthPathHandler.Handle(Context, token, odinContext);

                    case ClientTokenType.BuiltInBrowserApp:
                        return await HandleWithAnonymousFallback(
                            () => BuiltInBrowserAppHandler.Handle(Context, token, odinContext),
                            Context,
                            odinContext);

                    case ClientTokenType.YouAuth:
                        return await HandleWithAnonymousFallback(
                            () => GuestAuthPathHandler.Handle(Context, token, odinContext),
                            Context,
                            odinContext);

                    default:
                        return AuthenticateResult.Fail("Invalid Token Type");
                }
            }
            catch (OdinSecurityException e)
            {
                return AuthenticateResult.Fail(e.Message);
            }
        }

        public async Task SignOutAsync(AuthenticationProperties? properties)
        {
            if (properties == null)
            {
                return;
            }

            if (properties.Items.TryGetValue(nameof(ClientTokenType), out var value))
            {
                if (Enum.TryParse<ClientTokenType>(value, out var tt))
                {
                    var odinContext = Context.RequestServices.GetRequiredService<IOdinContext>();

                    switch (tt)
                    {
                        case ClientTokenType.BuiltInBrowserApp:
                            await BuiltInBrowserAppHandler.HandleSignOut(Context, odinContext);
                            break;

                        case ClientTokenType.YouAuth:
                            await GuestAuthPathHandler.HandleSignOut(Context, odinContext);
                            break;

                        case ClientTokenType.Owner:
                            var id = properties.GetParameter<Guid>(OwnerAuthConstants.CookieName);
                            await OwnerAuthPathHandler.HandleSignOut(Context, id);
                            break;

                        case ClientTokenType.App:
                            await AppAuthPathHandler.HandleSignOut(Context, odinContext);
                            break;
                    }
                }
            }
        }

        public Task SignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }

        //

        private async Task<AuthenticateResult> HandleWithAnonymousFallback(
            Func<Task<AuthenticateResult?>> handler,
            HttpContext context,
            IOdinContext odinContext)
        {
            var result = await handler();
            return result ?? await CreateAnonResult(context, odinContext);
        }

        
        private bool TryFindClientAuthToken(out ClientAuthenticationToken clientAuthToken)
        {
         
            if (_config.Cdn.Enabled)
            {
                if (TryGetClientAuthToken(this.Context, OdinHeaderNames.OdinCdnAuth, out clientAuthToken, preferHeader: true))
                {
                    return true;
                }
            }

            if (TryGetClientAuthToken(this.Context, UnifiedAuthConstants.CookieName, out clientAuthToken))
            {
                _localLogger.LogDebug("Using the Unified cookie");
                return true;
            }
            
            if (TryGetClientAuthToken(this.Context, OwnerAuthConstants.CookieName, out clientAuthToken))
            {
                return true;
            }

            if (TryGetClientAuthToken(this.Context, YouAuthConstants.AppCookieName, out clientAuthToken, true))
            {
                return true;
            }

            return TryGetClientAuthToken(this.Context, YouAuthDefaults.XTokenCookieName, out clientAuthToken);
        }

        private static bool TryGetClientAuthToken(HttpContext context, string cookieName, out ClientAuthenticationToken clientAuthToken,
            bool preferHeader = false)
        {
            var clientAccessTokenValue64 = string.Empty;
            if (preferHeader)
            {
                clientAccessTokenValue64 = context.Request.Headers[cookieName];
            }

            if (string.IsNullOrWhiteSpace(clientAccessTokenValue64))
            {
                clientAccessTokenValue64 = context.Request.Cookies[cookieName];
            }

            return ClientAuthenticationToken.TryParse(clientAccessTokenValue64, out clientAuthToken);
        }

        private static async Task<AuthenticateResult> CreateAnonResult(HttpContext httpContext, IOdinContext odinContext)
        {
            var driveManager = httpContext.RequestServices.GetRequiredService<IDriveManager>();
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

            var tenantContext = httpContext.RequestServices.GetRequiredService<TenantContext>();
            var anonPerms = new List<int>();

            if (tenantContext.Settings.AnonymousVisitorsCanViewConnections)
            {
                anonPerms.Add(PermissionKeys.ReadConnections);
            }

            if (tenantContext.Settings.AnonymousVisitorsCanViewWhoIFollow)
            {
                anonPerms.Add(PermissionKeys.ReadWhoIFollow);
            }

            anonPerms.Add(PermissionKeys.UseTransitRead);

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
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), YouAuthConstants.YouAuthScheme);
            return AuthenticateResult.Success(ticket);
        }
    }
}