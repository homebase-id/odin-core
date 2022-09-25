using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Provisioning;
using Youverse.Core.Services.Tenant;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Authentication.Perimeter;

namespace Youverse.Hosting.Middleware
{
    public class DotYouContextMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITenantProvider _tenantProvider;
        private readonly TenantConfigService _tenantProvisioner;

        public DotYouContextMiddleware(RequestDelegate next, ITenantProvider tenantProvider, TenantConfigService tenantProvisioner)
        {
            _next = next;
            _tenantProvider = tenantProvider;
            _tenantProvisioner = tenantProvisioner;
        }

        public async Task Invoke(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var tenant = _tenantProvider.GetCurrentTenant();
            string authType = httpContext.User.Identity?.AuthenticationType;

            if (tenant?.Name == null || string.IsNullOrEmpty(authType))
            {
                dotYouContext.Caller = new CallerContext(default, null, SecurityGroupType.Anonymous);
                await _next(httpContext);
                return;
            }

            if (authType == OwnerAuthConstants.SchemeName)
            {
                await LoadOwnerContext(httpContext, dotYouContext);
                dotYouContext.AuthContext = OwnerAuthConstants.SchemeName;

                await _next(httpContext);
                return;
            }

            if (authType == PerimeterAuthConstants.TransitCertificateAuthScheme)
            {
                await LoadTransitContext(httpContext, dotYouContext);
                dotYouContext.AuthContext = PerimeterAuthConstants.TransitCertificateAuthScheme;

                await _next(httpContext);
                return;
            }

            if (authType == PerimeterAuthConstants.PublicTransitAuthScheme)
            {
                await LoadPublicTransitContext(httpContext, dotYouContext);
                dotYouContext.AuthContext = PerimeterAuthConstants.PublicTransitAuthScheme;

                await _next(httpContext);
                return;
            }


            await _next(httpContext);
        }

        private async Task LoadOwnerContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var user = httpContext.User;

            var driveService = httpContext.RequestServices.GetRequiredService<IDriveService>();
            var authService = httpContext.RequestServices.GetRequiredService<IOwnerAuthenticationService>();
            var authResult = ClientAuthenticationToken.Parse(user.FindFirstValue(DotYouClaimTypes.AuthResult));
            var (masterKey, clientSharedSecret) = await authService.GetMasterKey(authResult.Id, authResult.AccessTokenHalfKey);

            dotYouContext.Caller = new CallerContext(
                dotYouId: (DotYouIdentity)user.Identity!.Name,
                masterKey: masterKey,
                securityLevel: SecurityGroupType.Owner);

            var allDrives = await driveService.GetDrives(PageOptions.All);
            var allDriveGrants = allDrives.Results.Select(d => new DriveGrant()
            {
                DriveId = d.Id,
                KeyStoreKeyEncryptedStorageKey = d.MasterKeyEncryptedStorageKey,
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = d.TargetDriveInfo,
                    Permission = DrivePermission.All
                },
            });

            //permission set is null because this is the owner
            var permissionGroupMap = new Dictionary<string, PermissionGroup>
            {
                //HACK: giving this the master key makes my hairs raise >:-[
                { "owner_drive_grants", new PermissionGroup(null, allDriveGrants, masterKey) },
            };

            dotYouContext.SetPermissionContext(
                new PermissionContext(
                    permissionGroupMap,
                    sharedSecretKey: clientSharedSecret,
                    isOwner: true
                ));
        }

        private async Task LoadTransitContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            var user = httpContext.User;
            var circleNetworkService = httpContext.RequestServices.GetRequiredService<ICircleNetworkService>();

            var callerDotYouId = (DotYouIdentity)user.Identity!.Name;
            dotYouContext.Caller = new CallerContext(
                dotYouId: callerDotYouId, //note: this will need to come from a claim: re: best buy/3rd party scenario
                masterKey: null,
                securityLevel: SecurityGroupType.Authenticated);

            if (ClientAuthenticationToken.TryParse(httpContext.Request.Headers[DotYouHeaderNames.ClientAuthToken], out var clientAuthToken))
            {
                var (permissionContext, circleIds) = await circleNetworkService.CreateTransitPermissionContext(callerDotYouId, clientAuthToken);
                dotYouContext.Caller.SecurityLevel = SecurityGroupType.Connected;
                dotYouContext.Caller.Circles = circleIds;
                dotYouContext.Caller.SetIsConnected();
                dotYouContext.SetPermissionContext(permissionContext);
            }
            else
            {
                dotYouContext.SetPermissionContext(null);
            }
        }

        private Task LoadPublicTransitContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            /*
             * handle these requests only -
             * Connection Requests Management
                give one permission to add a request
                give no drive permission
             * transit public key request (offline)
             */

            var user = httpContext.User;
            var callerDotYouId = (DotYouIdentity)user.Identity!.Name;

            dotYouContext.Caller = new CallerContext(
                dotYouId: callerDotYouId, //note: this will need to come from a claim: re: best buy/3rd party scenario
                masterKey: null,
                securityLevel: SecurityGroupType.Authenticated);

            //No permissions allowed
            dotYouContext.SetPermissionContext(null);

            return Task.CompletedTask;
        }
    }
}