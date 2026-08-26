using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Base;
using Odin.Services.Configuration;

namespace Odin.Hosting.Middleware
{
    /// <summary>
    /// Handles various scenarios to determine if the identity can be used. (Also checkout VersionUpgradeMiddleware)
    /// </summary>
    public class IdentityReadyStateMiddleware(RequestDelegate next)
    {
        // Note: the ready-state service is resolved from the request scope rather than injected as a
        // method parameter. Method-injected parameters are resolved on every request, before the path
        // checks below get a chance to short-circuit.
        public async Task InvokeAsync(HttpContext context)
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

            var identityReadyState = context.RequestServices.GetRequiredService<IdentityReadyStateService>();
            if(!await identityReadyState.IsIdentityServerConfiguredAsync())
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