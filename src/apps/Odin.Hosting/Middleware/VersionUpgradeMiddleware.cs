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
        public Task InvokeAsync(HttpContext context, VersionUpgradeScheduler scheduler, VersionUpgradeService upgradeService)
        {
            var path = context.Request.Path.Value;
            
            if (path == null)
            {
                return next(context);
            }

            if (!path.StartsWith("/api"))
            {
                return next(context);
            }

            if (path.Contains(OwnerConfigurationController.InitialSetupEndpoint))
            {
                return next(context);
            }

            if (path.StartsWith(OwnerApiPathConstants.AuthV1))
            {
                return next(context);
            }

            if (upgradeService.IsRunning())
            {
                context.Response.Headers.Append(OdinHeaderNames.UpgradeIsRunning, bool.TrueString);
                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                return Task.CompletedTask;
            }

            return next(context);
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