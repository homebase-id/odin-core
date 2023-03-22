using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

// Originally from StackOverflow:
// https://stackoverflow.com/questions/42199757/enable-options-header-for-cors-on-net-core-web-api
namespace Youverse.Hosting.Middleware
{

    public class OptionsMiddleware
    {
        private readonly RequestDelegate _next;

        public OptionsMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext context)
        {
            return BeginInvoke(context);
        }

        private Task BeginInvoke(HttpContext context)
        {
            if (context.Request.Method == "OPTIONS")
            {
                context.Response.Headers.Add("Access-Control-Allow-Origin",(string)context.Request.Headers["Origin"]);
                context.Response.Headers.Add("Access-Control-Allow-Headers",
                    new[] { "Content-Type", "Accept", "bx0900", "x-odin-file-system-type" });
                context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS" );
                context.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
                context.Response.Headers.Add("Access-Control-Expose-Headers", "*");
                context.Response.StatusCode = 200;
                return context.Response.WriteAsync("OK");
            } else if (context.Request.Path.StartsWithSegments("/api/apps/v1"))
            {
                context.Response.Headers.Add("Access-Control-Allow-Origin", (string)context.Request.Headers["Origin"]);
                context.Response.Headers.Add("Access-Control-Allow-Headers",
                    new[] { "bx0900", "x-odin-file-system-type" });
                context.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
                context.Response.Headers.Add("Access-Control-Expose-Headers",
                    new[] { "SharedSecretEncryptedHeader64","PayloadEncrypted" });
            }

            return _next.Invoke(context);
        }
    }
    public static class OptionsMiddlewareExtensions
    {
        public static IApplicationBuilder UseOptions(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<OptionsMiddleware>();
        }
    }
}
