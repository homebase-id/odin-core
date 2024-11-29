using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Odin.Services.Base;
using Odin.Services.Configuration;

namespace Odin.Hosting.Middleware
{
    /// <summary>
    /// Handles various scenarios to determine if the identity can be used. (Also checkout VersionUpgradeMiddleware)
    /// </summary>
    public class IdentityReadyStateMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context, IOdinContext odinContext, TenantConfigService tenantConfigService)
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

            if(!await tenantConfigService.IsIdentityServerConfiguredAsync())
            {
                context.Response.Headers.Append(OdinHeaderNames.RequiresInitialConfiguration, bool.TrueString);
            }

            await next(context);
        }
    }

    public static class IdentityReadyStateMiddlewareExtensions
    {
        public static IApplicationBuilder UseIdentityReadyState(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<IdentityReadyStateMiddleware>();
        }
    }
}