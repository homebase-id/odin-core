using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication.Transit;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Tenant;
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

        private async Task LoadTransitContext(HttpContext httpContext, DotYouContext dotYouContext)
        {
            if (ClientAuthenticationToken.TryParse(httpContext.Request.Headers[DotYouHeaderNames.ClientAuthToken],
                    out var clientAuthToken))
            {
                var user = httpContext.User;
                var transitRegService = httpContext.RequestServices.GetRequiredService<TransitRegistrationService>();
                var callerDotYouId = (DotYouIdentity)user.Identity!.Name;
                var ctx = await transitRegService.GetDotYouContext(callerDotYouId, clientAuthToken);

                if (ctx != null)
                {
                    dotYouContext.Caller = ctx.Caller;
                    dotYouContext.SetPermissionContext(ctx.PermissionsContext);
                }
            }

            await LoadPublicTransitContext(httpContext, dotYouContext);
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