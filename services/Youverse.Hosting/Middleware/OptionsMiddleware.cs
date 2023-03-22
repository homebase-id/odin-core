using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Youverse.Core.Services.Base;
using Youverse.Hosting.Authentication.ClientToken;
using Youverse.Hosting.Controllers.ClientToken;

// Originally from StackOverflow:
// https://stackoverflow.com/questions/42199757/enable-options-header-for-cors-on-net-core-web-api
namespace Youverse.Hosting.Middleware
{

    public class AppCorsMiddleware
    {
        private readonly RequestDelegate _next;

        public AppCorsMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext context)
        {
            return BeginInvoke(context);
        }

        private Task BeginInvoke(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments(AppApiPathConstants.BasePathV1) && context.Request.Headers["Origin"].Equals("https://dominion.id:3005"))
            {
                if (context.Request.Method == "OPTIONS")
                {
                    context.Response.Headers.Add("Access-Control-Allow-Origin",
                        (string)context.Request.Headers["Origin"]);
                    context.Response.Headers.Add("Access-Control-Allow-Headers",
                        new[] { "Content-Type", "Accept", ClientTokenConstants.ClientAuthTokenCookieName, DotYouHeaderNames.FileSystemTypeHeader });
                    context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                    context.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
                    context.Response.Headers.Add("Access-Control-Expose-Headers", "*");
                    context.Response.StatusCode = 200;
                    return context.Response.WriteAsync("OK");
                }
                else
                {
                    context.Response.Headers.Add("Access-Control-Allow-Origin",
                        (string)context.Request.Headers["Origin"]);
                    context.Response.Headers.Add("Access-Control-Allow-Headers",
                        new[] { ClientTokenConstants.ClientAuthTokenCookieName, DotYouHeaderNames.FileSystemTypeHeader });
                    context.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
                    context.Response.Headers.Add("Access-Control-Expose-Headers",
                        new[] { HttpHeaderConstants.SharedSecretEncryptedHeader64, HttpHeaderConstants.PayloadEncrypted });
                }
            }

            return _next.Invoke(context);
        }
    }
    public static class AppCorsMiddlewareExtensions
    {
        public static IApplicationBuilder UseAppCors(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AppCorsMiddleware>();
        }
    }
}
