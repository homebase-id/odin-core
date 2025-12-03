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
using Odin.Hosting.UnifiedV2.Authentication.Policy;
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

        private static readonly IAuthPathHandler OwnerHandler = new OwnerAuthPathHandler();
        private static readonly IAuthPathHandler GuestHandler = new GuestAuthPathHandler();
        private static readonly IAuthPathHandler BuiltInHandler = new BuiltInBrowserAppHandler();
        private static readonly IAuthPathHandler AppHandler = new AppAuthPathHandler();
        private static readonly IAuthPathHandler CdnHandler = new CdnAuthPathHandler();

        /// <summary/>
        public UnifiedAuthenticationHandler(IOptionsMonitor<UnifiedAuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, OdinConfiguration config)
            : base(options, logger, encoder)
        {
            _config = config;
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

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var odinContext = Context.RequestServices.GetRequiredService<IOdinContext>();

            if (!TryFindClientAuthToken(out var token))
            {
                return await CreateAnonResult(Context, odinContext);
            }

            try
            {
                var handler = GetHandler(token.ClientTokenType);

                if (handler == null)
                {
                    return AuthenticateResult.Fail("Invalid Token Type");
                }

                var result = await handler.HandleAsync(Context, token, odinContext);

                odinContext.SetAuthContext(UnifiedAuthConstants.SchemeName);

                switch (result.Status)
                {
                    case AuthHandlerStatus.Success:
                        return CreateSuccessResult(result.Claims!, token, odinContext);

                    case AuthHandlerStatus.AnonymousFallback:
                        return await CreateAnonResult(Context, odinContext);

                    case AuthHandlerStatus.Fail:
                    default:
                        return AuthenticateResult.Fail("Invalid Token");
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
                return;

            var typeString = properties.GetParameter<string>("clientTokenType");
            if (typeString == null)
                return;

            if (!Enum.TryParse<ClientTokenType>(typeString, out var tokenType))
                return;

            var handler = GetHandler(tokenType);
            if (handler == null)
                return;

            var odinContext = Context.RequestServices.GetRequiredService<IOdinContext>();

            var tokenId = properties.GetParameter<Guid?>("tokenId");
            await handler.HandleSignOutAsync(tokenId.GetValueOrDefault(), Context, odinContext);
        }

        public Task SignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }

        //

        private static AuthenticateResult CreateSuccessResult(
            List<Claim> claims,
            ClientAuthenticationToken token, 
            IOdinContext odinContext)
        {
            var scheme = UnifiedAuthConstants.SchemeName;
            
            claims.Add(new Claim(ClaimTypes.Name, odinContext.GetCallerOdinIdOrFail()));
            claims.Add(new Claim(UnifiedClaimTypes.ClientTokenType,
                UnifiedPolicies.AsClaimValue(token.ClientTokenType),
                ClaimValueTypes.Integer32,
                UnifiedClaimTypes.Issuer));

            var claimsIdentity = new ClaimsIdentity(claims, scheme);

            var props = new AuthenticationProperties();
            props.SetParameter("tokenId", token.Id);
            props.SetParameter("clientTokenType", token.ClientTokenType.ToString());

            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(claimsIdentity),
                props,
                scheme);

            return AuthenticateResult.Success(ticket);
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

            if (TryGetClientAuthToken(this.Context, OwnerAuthConstants.CookieName, out clientAuthToken))
            {
                return true;
            }

            if (TryGetClientAuthToken(this.Context, YouAuthConstants.AppCookieName, out clientAuthToken, preferHeader: true))
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

        private static IAuthPathHandler? GetHandler(ClientTokenType type)
        {
            switch (type)
            {
                case ClientTokenType.Owner:
                    return OwnerHandler;

                case ClientTokenType.YouAuth:
                    return GuestHandler;

                case ClientTokenType.BuiltInBrowserApp:
                    return BuiltInHandler;

                case ClientTokenType.App:
                    return AppHandler;

                case ClientTokenType.Cdn:
                    return CdnHandler;

                default:
                    return null;
            }
        }
    }
}