using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication.Transit;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.DataSubscription;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Services.Tenant;
using Youverse.Hosting.Authentication.Perimeter;

namespace Youverse.Hosting.Middleware
{
    /// <summary/>
    public class OdinContextMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITenantProvider _tenantProvider;

        /// <summary/>
        public OdinContextMiddleware(RequestDelegate next, ITenantProvider tenantProvider)
        {
            _next = next;
            _tenantProvider = tenantProvider;
        }

        /// <summary/>
        public async Task Invoke(HttpContext httpContext, OdinContext odinContext)
        {
            var tenant = _tenantProvider.GetCurrentTenant();
            string authType = httpContext.User.Identity?.AuthenticationType;

            odinContext.Tenant = (OdinId)tenant?.Name;

            if (string.IsNullOrEmpty(authType))
            {
                odinContext.Caller = new CallerContext(default, null, SecurityGroupType.Anonymous);
                await _next(httpContext);
                return;
            }
            
            if (authType == PerimeterAuthConstants.TransitCertificateAuthScheme)
            {
                await LoadTransitContext(httpContext, odinContext);

                await _next(httpContext);
                return;
            }

            if (authType == PerimeterAuthConstants.FeedAuthScheme)
            {
                await LoadIdentitiesIFollowContext(httpContext, odinContext);
                await _next(httpContext);
                return;
            }

            // if (authType == PerimeterAuthConstants.FollowerCertificateAuthScheme)
            // {
            //     await LoadFollowerContext(httpContext, dotYouContext);
            //     dotYouContext.AuthContext = PerimeterAuthConstants.FollowerCertificateAuthScheme;
            //     await _next(httpContext);
            //     return;
            // }

            if (authType == PerimeterAuthConstants.PublicTransitAuthScheme)
            {
                await LoadPublicTransitContext(httpContext, odinContext);
                await _next(httpContext);
                return;
            }

            await _next(httpContext);
        }

        private async Task LoadTransitContext(HttpContext httpContext, OdinContext odinContext)
        {
            if (ClientAuthenticationToken.TryParse(httpContext.Request.Headers[OdinHeaderNames.ClientAuthToken], out var clientAuthToken))
            {
                //HACK - for alpha, wen want to support data subscriptions for the feed but only building it partially
                //therefore use the transit subsystem but load permissions only for the fee drive
                // if (clientAuthToken.ClientTokenType == ClientTokenType.DataProvider)
                // {
                //     await LoadIdentitiesIFollowContext(httpContext, dotYouContext);
                //     return;
                // }

                if (clientAuthToken.ClientTokenType == ClientTokenType.Follower)
                {
                    await LoadFollowerContext(httpContext, odinContext);
                    return;
                }

                var user = httpContext.User;
                var transitRegService = httpContext.RequestServices.GetRequiredService<TransitAuthenticationService>();
                var callerOdinId = (OdinId)user.Identity!.Name;
                var ctx = await transitRegService.GetDotYouContext(callerOdinId, clientAuthToken);

                if (ctx != null)
                {
                    odinContext.Caller = ctx.Caller;
                    odinContext.SetPermissionContext(ctx.PermissionsContext);
                    odinContext.SetAuthContext(PerimeterAuthConstants.TransitCertificateAuthScheme);
                    return;
                }
            }

            await LoadPublicTransitContext(httpContext, odinContext);
        }

        private async Task LoadIdentitiesIFollowContext(HttpContext httpContext, OdinContext odinContext)
        {
            //No token for now
            var user = httpContext.User;
            var authService = httpContext.RequestServices.GetRequiredService<IdentitiesIFollowAuthenticationService>();
            var odinId = (OdinId)user.Identity!.Name;
            var ctx = await authService.GetDotYouContext(odinId, null);
            if (ctx != null)
            {
                odinContext.Caller = ctx.Caller;
                odinContext.SetPermissionContext(ctx.PermissionsContext);
                odinContext.SetAuthContext(PerimeterAuthConstants.FeedAuthScheme);

                return;
            }

            throw new YouverseSecurityException("Cannot load context");
        }

        private async Task LoadFollowerContext(HttpContext httpContext, OdinContext odinContext)
        {
            //No token for now
            if (ClientAuthenticationToken.TryParse(httpContext.Request.Headers[OdinHeaderNames.ClientAuthToken], out var clientAuthToken))
            {
                var user = httpContext.User;
                var odinId = (OdinId)user.Identity!.Name;
                var authService = httpContext.RequestServices.GetRequiredService<FollowerAuthenticationService>();
                var ctx = await authService.GetDotYouContext(odinId, clientAuthToken);

                //load in transit context
                // var transitRegService = httpContext.RequestServices.GetRequiredService<TransitRegistrationService>();
                // var transitCtx = await transitRegService.GetDotYouContext(odinId, clientAuthToken);

                // transitCtx.PermissionsContext.Redacted().PermissionGroups.First().DriveGrants.First().PermissionedDrive.

                if (ctx != null)
                {
                    odinContext.Caller = ctx.Caller;
                    odinContext.SetPermissionContext(ctx.PermissionsContext);
                    odinContext.SetAuthContext(PerimeterAuthConstants.FollowerCertificateAuthScheme);
                    return;
                }
            }

            throw new YouverseSecurityException("Cannot load context");
        }

        private async Task LoadPublicTransitContext(HttpContext httpContext, OdinContext odinContext)
        {
            var user = httpContext.User;
            var odinId = (OdinId)user.Identity!.Name;

            odinContext.Caller = new CallerContext(
                odinId: odinId,
                masterKey: null,
                securityLevel: SecurityGroupType.Authenticated);

            var driveManager = httpContext.RequestServices.GetRequiredService<DriveManager>();
            var anonymousDrives = await driveManager.GetAnonymousDrives(PageOptions.All);

            if (!anonymousDrives.Results.Any())
            {
                throw new YouverseClientException(
                    "No anonymous drives configured.  There should be at least one; be sure you accessed /owner to initialize them.",
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

            var permissionGroupMap = new Dictionary<string, PermissionGroup>
            {
                { "read_anonymous_drives", new PermissionGroup(new PermissionSet(), anonDriveGrants, null) },
            };

            odinContext.SetPermissionContext(
                new PermissionContext(
                    permissionGroupMap,
                    sharedSecretKey: null
                ));

            odinContext.SetAuthContext(PerimeterAuthConstants.PublicTransitAuthScheme);
        }
    }
}