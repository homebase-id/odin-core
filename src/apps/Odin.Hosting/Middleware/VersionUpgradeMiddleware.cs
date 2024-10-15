using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade;

namespace Odin.Hosting.Middleware
{
    public class VersionUpgradeMiddleware(RequestDelegate next, VersionUpgradeScheduler scheduler)
    {
        public Task Invoke(HttpContext context, IOdinContext odinContext)
        {
            if (scheduler.RequiresUpgrade())
            {
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