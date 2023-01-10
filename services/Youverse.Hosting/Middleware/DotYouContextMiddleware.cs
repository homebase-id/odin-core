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
using Youverse.Core.Services.Tenant;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Authentication.Perimeter;

namespace Youverse.Hosting.Middleware
{
    public class DotYouContextMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITenantProvider _tenantProvider;

        public DotYouContextMiddleware(RequestDelegate next, ITenantProvider tenantProvider)
        {
            _next = next;
            _tenantProvider = tenantProvider;
        }

        public async Task Invoke(HttpContext httpContext, DotYouContext dotYouContext,TransitContextCache transitContextCache)
        {
            var tenant = _tenantProvider.GetCurrentTenant();
            string authType = httpContext.User.Identity?.AuthenticationType;

            if (tenant?.Name == null || string.IsNullOrEmpty(authType))
            {
                dotYouContext.Caller = new CallerContext(default, null, SecurityGroupType.Anonymous);
                await _next(httpContext);
                return;
            }

            if (authType == PerimeterAuthConstants.TransitCertificateAuthScheme)
            {
                await LoadTransitContext(httpContext, dotYouContext, transitContextCache);
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

        private async Task LoadTransitContext(HttpContext httpContext, DotYouContext dotYouContext,
            TransitContextCache transitContextCache)
        {
            var user = httpContext.User;
            var circleNetworkService = httpContext.RequestServices.GetRequiredService<ICircleNetworkService>();

            var callerDotYouId = (DotYouIdentity)user.Identity!.Name;
            dotYouContext.Caller = new CallerContext(
                dotYouId: callerDotYouId, //note: this will need to come from a claim: re: best buy/3rd party scenario
                masterKey: null,
                securityLevel: SecurityGroupType.Authenticated);


            if (ClientAuthenticationToken.TryParse(httpContext.Request.Headers[DotYouHeaderNames.ClientAuthToken],
                    out var clientAuthToken))
            {
                // var ctx = transitContextCache.GetOrAdd(clientAuthToken, token =>
                // {
                //     var (permissionContext, circleIds) = circleNetworkService
                //         .CreateTransitPermissionContext(callerDotYouId, token).GetAwaiter().GetResult();
                //     dotYouContext.Caller.SecurityLevel = SecurityGroupType.Connected;
                //     dotYouContext.Caller.Circles = circleIds;
                //     dotYouContext.Caller.SetIsConnected();
                //     dotYouContext.SetPermissionContext(permissionContext);
                //     return dotYouContext;
                // });
                //
                // dotYouContext.Caller = ctx.Caller;
                // //dotYouContext.SetPermissionContext(ctx.PermissionsContext);
                //
                //check dotyoucontxt cache for transit
                // if (transitContextCache.TryGetCachedContext(clientAuthToken, out var ctx))
                // {
                //     dotYouContext.Caller = ctx.Caller;
                //     dotYouContext.SetPermissionContext(ctx.PermissionsContext);
                //     
                //     dotYouContext.Caller.SetIsConnected();
                // }
                // else
                // {
                    var (permissionContext, circleIds) =
                        await circleNetworkService.CreateTransitPermissionContext(callerDotYouId, clientAuthToken);
                    dotYouContext.Caller.SecurityLevel = SecurityGroupType.Connected;
                    dotYouContext.Caller.Circles = circleIds;
                    dotYouContext.Caller.SetIsConnected();
                    dotYouContext.SetPermissionContext(permissionContext);

                    // transitContextCache.CacheContext(clientAuthToken, dotYouContext);
                // }
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