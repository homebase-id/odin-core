using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Org.BouncyCastle.Tls;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Registry.Registration;
using Youverse.Hosting.Authentication.ClientToken;

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
            if (context.Request.Method == "OPTIONS")
            {
                //handled by a controller
                return _next.Invoke(context);
            }

            bool shouldSetHeaders = false;

            List<string> allowHeaders = new List<string>();

            if (dotYouContext.AuthContext == ClientTokenConstants.AppSchemeName)
            {
                string appHostName = dotYouContext.Caller.AppContext.CorsAppName;
                if (!string.IsNullOrEmpty(appHostName))
                {
                    shouldSetHeaders = true;
                    context.Response.Headers.Add("Access-Control-Allow-Origin", $"https://{appHostName}");
                    allowHeaders.Add(ClientTokenConstants.ClientAuthTokenCookieName);
                    allowHeaders.Add(DotYouHeaderNames.FileSystemTypeHeader);
                }
            }
            else if ( //if the browser gives me an origin that is this identity (i.e. the home or owner app) 
                string.Equals(context.Request.Headers["Origin"], $"https://{dotYouContext.Tenant.DomainName}", StringComparison.InvariantCultureIgnoreCase) &&
                string.Equals(context.Request.Host.Host, $"{DnsConfigurationSet.PrefixApi}.{dotYouContext.Tenant.DomainName}", StringComparison.InvariantCultureIgnoreCase))
            {
                context.Response.Headers.Add("Access-Control-Allow-Origin", $"https://{dotYouContext.Tenant.DomainName}");
                allowHeaders.Add(DotYouHeaderNames.FileSystemTypeHeader);
                shouldSetHeaders = true;
            }

            if (shouldSetHeaders)
            {
                context.Response.Headers.Add("Access-Control-Allow-Credentials", new[] { "true" });
                context.Response.Headers.Add("Access-Control-Allow-Headers", allowHeaders.ToArray());
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
