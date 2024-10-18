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
        public Task Invoke(HttpContext context, IOdinContext odinContext, VersionUpgradeScheduler scheduler)
        {
            var path = context.Request.Path.Value;

            if (path == null)
            {
                return next.Invoke(context);
            }

            if (!path.StartsWith("/api"))
            {
                return next.Invoke(context);
            }

            if (path.Contains(OwnerConfigurationController.InitialSetupEndpoint))
            {
                return next.Invoke(context);
            }

            if (path.StartsWith(OwnerApiPathConstants.AuthV1))
            {
                return next.Invoke(context);
            }

            if (scheduler.RequiresUpgrade())
            {
                context.Response.Headers.Append(OdinHeaderNames.RequiresUpgrade, bool.TrueString);
                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                return Task.CompletedTask;
            }

            return next.Invoke(context);
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