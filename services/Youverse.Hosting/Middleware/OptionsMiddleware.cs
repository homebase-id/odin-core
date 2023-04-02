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

        public Task Invoke(HttpContext context, DotYouContext dotYouContext)
        {
            return BeginInvoke(context, dotYouContext);
        }

        private Task BeginInvoke(HttpContext context, DotYouContext dotYouContext)
        {
            var originHeader = context.Request.Headers["Origin"];
            // if (context.Request.Path.StartsWithSegments(AppApiPathConstants.BasePathV1) &&
            //     (originHeader.Equals("https://dominion.id:3005") || originHeader.Equals("https://photos.odin.earth")))
         
            if(dotYouContext is { AuthContext: ClientTokenConstants.AppSchemeName } && context.Request.Method.ToUpper() != "OPTIONS")
            {
                // context.Response.Headers.Add("Access-Control-Allow-Origin", originHeader);

                string appHostName = dotYouContext.Caller.AppContext.CorsAppName;
                if (!string.IsNullOrEmpty(appHostName))
                {
                    context.Response.Headers.Add("Access-Control-Allow-Origin", $"https://{appHostName}");
                }

                context.Response.Headers.Add("Access-Control-Allow-Headers",
                    new[]
                    {
                        ClientTokenConstants.ClientAuthTokenCookieName, DotYouHeaderNames.FileSystemTypeHeader
                    });
                context.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
                context.Response.Headers.Add("Access-Control-Expose-Headers",
                    new[]
                    {
                        HttpHeaderConstants.SharedSecretEncryptedHeader64, HttpHeaderConstants.PayloadEncrypted
                    });
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