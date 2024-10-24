﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Authentication.Transit;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.DataSubscription;
using Odin.Services.Drives.Management;
using Odin.Services.Tenant;
using Odin.Hosting.Authentication.Peer;

namespace Odin.Hosting.Middleware
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
        public async Task Invoke(HttpContext httpContext, IOdinContext odinContext, TenantSystemStorage tenantSystemStorage)
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

            if (authType == PeerAuthConstants.TransitCertificateAuthScheme)
            {
                var db = tenantSystemStorage.IdentityDatabase;
                await LoadTransitContext(httpContext, odinContext, db);
                await _next(httpContext);
                return;
            }

            if (authType == PeerAuthConstants.FeedAuthScheme)
            {
                var db = tenantSystemStorage.IdentityDatabase;
                await LoadIdentitiesIFollowContext(httpContext, odinContext, db);
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

            if (authType == PeerAuthConstants.PublicTransitAuthScheme)
            {
                var db = tenantSystemStorage.IdentityDatabase;
                await LoadPublicTransitContext(httpContext, odinContext, db);
                await _next(httpContext);
                return;
            }

            await _next(httpContext);
        }

        private async Task LoadTransitContext(HttpContext httpContext, IOdinContext odinContext, IdentityDatabase db)
        {
            if (ClientAuthenticationToken.TryParse(httpContext.Request.Headers[OdinHeaderNames.ClientAuthToken], out var clientAuthToken))
            {
                //TODO: this appears to be a dead code path
                if (clientAuthToken.ClientTokenType == ClientTokenType.Follower)
                {
                    await LoadFollowerContext(httpContext, odinContext, db);
                    return;
                }

                try
                {
                    var user = httpContext.User;
                    var transitRegService = httpContext.RequestServices.GetRequiredService<TransitAuthenticationService>();
                    var callerOdinId = (OdinId)user.Identity!.Name;
                    var ctx = await transitRegService.GetDotYouContext(callerOdinId, clientAuthToken,odinContext, db);

                    if (ctx != null)
                    {
                        odinContext.Caller = ctx.Caller;
                        odinContext.SetPermissionContext(ctx.PermissionsContext);
                        odinContext.SetAuthContext(PeerAuthConstants.TransitCertificateAuthScheme);
                        return;
                    }
                }
                catch (OdinSecurityException e)
                {
                    if (e.IsRemoteIcrIssue)
                    {
                        httpContext.Response.Headers.Append(HttpHeaderConstants.RemoteServerIcrIssue, bool.TrueString);
                        //tell the caller and fall back to public files only
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            await LoadPublicTransitContext(httpContext, odinContext, db);
        }

        private async Task LoadIdentitiesIFollowContext(HttpContext httpContext, IOdinContext odinContext, IdentityDatabase db)
        {
            //No token for now
            var user = httpContext.User;
            var authService = httpContext.RequestServices.GetRequiredService<IdentitiesIFollowAuthenticationService>();
            var odinId = (OdinId)user.Identity!.Name;
            var ctx = await authService.GetDotYouContext(odinId, null, db);
            if (ctx != null)
            {
                odinContext.Caller = ctx.Caller;
                odinContext.SetPermissionContext(ctx.PermissionsContext);
                odinContext.SetAuthContext(PeerAuthConstants.FeedAuthScheme);

                return;
            }

            throw new OdinSecurityException("Cannot load context");
        }

        private async Task LoadFollowerContext(HttpContext httpContext, IOdinContext odinContext, IdentityDatabase db)
        {
            //No token for now
            if (ClientAuthenticationToken.TryParse(httpContext.Request.Headers[OdinHeaderNames.ClientAuthToken], out var clientAuthToken))
            {
                var user = httpContext.User;
                var odinId = (OdinId)user.Identity!.Name;
                var authService = httpContext.RequestServices.GetRequiredService<FollowerAuthenticationService>();
                var ctx = await authService.GetDotYouContext(odinId, clientAuthToken, db);

                if (ctx != null)
                {
                    odinContext.Caller = ctx.Caller;
                    odinContext.SetPermissionContext(ctx.PermissionsContext);
                    odinContext.SetAuthContext(PeerAuthConstants.FollowerCertificateAuthScheme);
                    return;
                }
            }

            throw new OdinSecurityException("Cannot load context");
        }

        private async Task LoadPublicTransitContext(HttpContext httpContext, IOdinContext odinContext, IdentityDatabase db)
        {
            var user = httpContext.User;
            var odinId = (OdinId)user.Identity!.Name;

            odinContext.Caller = new CallerContext(
                odinId: odinId,
                masterKey: null,
                securityLevel: SecurityGroupType.Authenticated);

            var driveManager = httpContext.RequestServices.GetRequiredService<DriveManager>();
            var anonymousDrives = await driveManager.GetAnonymousDrives(PageOptions.All,odinContext, db);

            if (!anonymousDrives.Results.Any())
            {
                throw new OdinClientException(
                    "No anonymous drives configured.  There should be at least one; be sure you accessed /owner to initialize them.",
                    OdinClientErrorCode.NotInitialized);
            }

            //TODO: need to put this behind a class and cache

            var tenantContext = httpContext.RequestServices.GetRequiredService<TenantContext>();
            var permissionKeys = tenantContext.Settings.GetAdditionalPermissionKeysForAuthenticatedIdentities();
            var anonDrivePermissions = tenantContext.Settings.GetAnonymousDrivePermissionsForAuthenticatedIdentities();

            var anonDriveGrants = anonymousDrives.Results.Select(d => new DriveGrant()
            {
                DriveId = d.Id,
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = d.TargetDriveInfo,
                    Permission = anonDrivePermissions
                }
            }).ToList();

            var permissionGroupMap = new Dictionary<string, PermissionGroup>
            {
                { "read_anonymous_drives", new PermissionGroup(new PermissionSet(permissionKeys), anonDriveGrants, null, null) },
            };

            odinContext.SetPermissionContext(
                new PermissionContext(
                    permissionGroupMap,
                    sharedSecretKey: null
                ));

            odinContext.SetAuthContext(PeerAuthConstants.PublicTransitAuthScheme);
        }
    }
}