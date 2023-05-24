using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Registry.Registration;
using Youverse.Hosting.Authentication.ClientToken;

// Sorry Todd, I know you just cleaned up the AppCorsMiddleware and here I am doing it all over
// again to support the api.* domains.. But to debug it all we need all these nasty headers once again
namespace Youverse.Hosting.Middleware
{
    public class ApiCorsMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiCorsMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext context, DotYouContext dotYouContext)
        {
            return BeginInvoke(context, dotYouContext);
        }

        private Task BeginInvoke(HttpContext context, DotYouContext dotYouContext)
        {
            if (dotYouContext is { AuthContext: ClientTokenConstants.AppSchemeName } || context.Request.Method == "OPTIONS")
            {
                return _next.Invoke(context);
            }

            // TODO: Let only work for the identity
            // Only if origin is frodo.dotyou.cloud and the request is for api.frodo.dotyou.cloud
            if (context.Request.Headers.ContainsKey("Origin"))
            {
                var originHeader = context.Request.Headers["Origin"];
                context.Response.Headers.Add("Access-Control-Allow-Origin", originHeader);
                context.Response.Headers.Add("Access-Control-Allow-Credentials", new[] { "true" });
                context.Response.Headers.Add("Access-Control-Allow-Headers", new[] { "Origin, Content-Type, Accept, X-ODIN-FILE-SYSTEM-TYPE" });
                context.Response.Headers.Add("Access-Control-Expose-Headers",
                    new[]
                    {
                        HttpHeaderConstants.SharedSecretEncryptedHeader64, HttpHeaderConstants.PayloadEncrypted, HttpHeaderConstants.DecryptedContentType
                    });
            }

            return _next.Invoke(context);
        }
    }

    public static class ApiCorsMiddlewareExtensions
    {
        public static IApplicationBuilder UseApiCors(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ApiCorsMiddleware>();
        }
    }
}
