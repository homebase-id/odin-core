using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Odin.Hosting.ApiExceptions.Server;
using Odin.Services.Background.Services.Tenant;
using Odin.Services.Base;

namespace Odin.Hosting.Middleware
{
    public class VersionUpgradeMiddleware(RequestDelegate next, VersionUpgradeContext versionContext)
    {
        public Task Invoke(HttpContext context, IOdinContext odinContext)
        {
            if (versionContext.IsRunning)
            {
                throw new ServiceUnavailableException("Version Upgrade in progress");
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