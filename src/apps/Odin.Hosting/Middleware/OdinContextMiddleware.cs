﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Authentication.Transit;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.DataSubscription;
using Odin.Services.Drives.Management;
using Odin.Services.Tenant;
using Odin.Hosting.Authentication.Peer;
using Odin.Services.Drives;

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
        public async Task Invoke(HttpContext httpContext, IOdinContext odinContext)
        {
            var tenant = _tenantProvider.GetCurrentTenant();
            string authType = httpContext.User.Identity?.AuthenticationType;

            odinContext.Tenant = (OdinId)tenant?.Name;

            if (string.IsNullOrEmpty(authType))
            {
                var logger = httpContext.RequestServices.GetService<ILogger<OdinContextMiddleware>>();

                try
                {
                    await LoadAnonymousContextAsync(httpContext, odinContext);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed loading anonymous context.  Note: the code execution continues on since this " +
                                       "was only created to support link-preview. however, have a look and fix me.  error message [{msg}]",
                        e.Message);
                }
                finally
                {
                    await _next(httpContext);
                }

                return;
            }

            if (authType == PeerAuthConstants.TransitCertificateAuthScheme)
            {
                await LoadTransitContextAsync(httpContext, odinContext);
                await _next(httpContext);
                return;
            }

            if (authType == PeerAuthConstants.FeedAuthScheme)
            {
                await LoadIdentitiesIFollowContextAsync(httpContext, odinContext);
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
                await LoadPublicTransitContextAsync(httpContext, odinContext);
                await _next(httpContext);
                return;
            }

            await _next(httpContext);
        }

        private async Task LoadTransitContextAsync(HttpContext httpContext, IOdinContext odinContext)
        {
            if (ClientAuthenticationToken.TryParse(httpContext.Request.Headers[OdinHeaderNames.ClientAuthToken], out var clientAuthToken))
            {
                //TODO: this appears to be a dead code path
                if (clientAuthToken.ClientTokenType == ClientTokenType.Follower)
                {
                    await LoadFollowerContextAsync(httpContext, odinContext);
                    return;
                }

                try
                {
                    var user = httpContext.User;
                    var transitRegService = httpContext.RequestServices.GetRequiredService<TransitAuthenticationService>();
                    var callerOdinId = (OdinId)user.Identity!.Name;
                    var ctx = await transitRegService.GetDotYouContextAsync(callerOdinId, clientAuthToken, odinContext);

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
                        //tell the caller and fall back to public files only
                        httpContext.Response.Headers.Append(HttpHeaderConstants.RemoteServerIcrIssue, bool.TrueString);
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            await LoadPublicTransitContextAsync(httpContext, odinContext);
        }

        private async Task LoadIdentitiesIFollowContextAsync(HttpContext httpContext, IOdinContext odinContext)
        {
            //No token for now
            var user = httpContext.User;
            var authService = httpContext.RequestServices.GetRequiredService<IdentitiesIFollowAuthenticationService>();
            var odinId = (OdinId)user.Identity!.Name;
            var ctx = await authService.GetDotYouContextAsync(odinId, null);
            if (ctx != null)
            {
                odinContext.Caller = ctx.Caller;
                odinContext.SetPermissionContext(ctx.PermissionsContext);
                odinContext.SetAuthContext(PeerAuthConstants.FeedAuthScheme);

                return;
            }

            throw new OdinSecurityException("Cannot load context");
        }

        private async Task LoadFollowerContextAsync(HttpContext httpContext, IOdinContext odinContext)
        {
            //No token for now
            if (ClientAuthenticationToken.TryParse(httpContext.Request.Headers[OdinHeaderNames.ClientAuthToken], out var clientAuthToken))
            {
                var user = httpContext.User;
                var odinId = (OdinId)user.Identity!.Name;
                var authService = httpContext.RequestServices.GetRequiredService<FollowerAuthenticationService>();
                var ctx = await authService.GetDotYouContextAsync(odinId, clientAuthToken);

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

        private async Task LoadPublicTransitContextAsync(HttpContext httpContext, IOdinContext odinContext)
        {
            var user = httpContext.User;
            var odinId = (OdinId)user.Identity!.Name;

            odinContext.Caller = new CallerContext(
                odinId: odinId,
                masterKey: null,
                securityLevel: SecurityGroupType.Authenticated);

            var driveManager = httpContext.RequestServices.GetRequiredService<DriveManager>();
            var anonymousDrives = await driveManager.GetAnonymousDrivesAsync(PageOptions.All, odinContext);

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

        private async Task LoadAnonymousContextAsync(HttpContext httpContext, IOdinContext odinContext)
        {
            odinContext.Caller = new CallerContext(
                odinId: default,
                masterKey: null,
                securityLevel: SecurityGroupType.Anonymous);

            var driveManager = httpContext.RequestServices.GetRequiredService<DriveManager>();
            var anonymousDrives = await driveManager.GetAnonymousDrivesAsync(PageOptions.All, odinContext);

            if (!anonymousDrives.Results.Any())
            {
                return;
            }

            //TODO: need to put this behind a class and cache

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
                { "read_anonymous_drives", new PermissionGroup(new PermissionSet(), anonDriveGrants, null, null) },
            };

            odinContext.SetPermissionContext(
                new PermissionContext(
                    permissionGroupMap,
                    sharedSecretKey: null
                ));
        }
    }
}