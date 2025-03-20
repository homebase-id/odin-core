using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Odin.Hosting.Controllers.OwnerToken.Configuration;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade;

namespace Odin.Hosting.Middleware
{
    public class VersionUpgradeMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context, VersionUpgradeScheduler scheduler, VersionUpgradeService upgradeService)
        {
            var path = context.Request.Path.Value;
            
            if (path == null)
            {
                await next(context);
                return;
            }

            if (!path.StartsWith("/api"))
            {
                await next(context);
                return;
            }

            if (path.Contains(OwnerConfigurationController.InitialSetupEndpoint))
            {
                await next(context);
                return;
            }

            if (path.StartsWith(OwnerApiPathConstants.AuthV1))
            {
                await next(context);
                return;
            }

            if (upgradeService.IsRunning())
            {
                context.Response.Headers.Append(OdinHeaderNames.UpgradeIsRunning, bool.TrueString);
                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                return;
            }

            await next(context);
        }
    }

    public static class VersionUpgradeMiddlewareExtensions
    {
        public static IApplicationBuilder UseVersionUpgrade(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<VersionUpgradeMiddleware>();
        }
    }
}