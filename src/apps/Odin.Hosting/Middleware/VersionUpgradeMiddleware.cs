using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Hosting.Controllers.OwnerToken.Configuration;
using Odin.Hosting.Controllers.OwnerToken.DataConversion;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade;

namespace Odin.Hosting.Middleware
{
    public class VersionUpgradeMiddleware(RequestDelegate next)
    {
        // Note: the run state is resolved from the request scope rather than injected as a method
        // parameter. Method-injected parameters are resolved on every request, before the path checks
        // below get a chance to short-circuit. (VersionUpgradeScheduler was injected but never used.)
        public Task InvokeAsync(HttpContext context)
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

            var runState = context.RequestServices.GetRequiredService<VersionUpgradeRunState>();
            if (runState.IsRunning)
            {
                context.Response.Headers.Append(OdinHeaderNames.UpgradeIsRunning, bool.TrueString);

                // The version-info endpoint is how a client finds out what the upgrade is doing, and
                // blocking it means the one question worth asking during an upgrade is the one that
                // cannot be asked. It reads two config values and writes nothing. The rest of the
                // data-conversion controller mutates, so it stays behind the guard.
                if (!path.Contains(OwnerDataConversionController.VersionInfoEndpoint))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                    return Task.CompletedTask;
                }
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