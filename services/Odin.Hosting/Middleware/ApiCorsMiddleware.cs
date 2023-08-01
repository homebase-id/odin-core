using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Odin.Core.Services.Base;
using Odin.Core.Services.Registry.Registration;
using Odin.Hosting.Authentication.ClientToken;

namespace Odin.Hosting.Middleware
{
    public class ApiCorsMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiCorsMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext context, OdinContext odinContext)
        {
            return BeginInvoke(context, odinContext);
        }

        private Task BeginInvoke(HttpContext context, OdinContext odinContext)
        {
            if (context.Request.Method == "OPTIONS")
            {
                //handled by a controller
                return _next.Invoke(context);
            }

            bool shouldSetHeaders = false;

            List<string> allowHeaders = new List<string>();

            if (odinContext.AuthContext == ClientTokenConstants.AppSchemeName)
            {
                string appHostName = odinContext.Caller.YouAuthContext.CorsHostName;
                if (!string.IsNullOrEmpty(appHostName))
                {
                    shouldSetHeaders = true;
                    context.Response.Headers.Add("Access-Control-Allow-Origin", $"https://{appHostName}");
                    allowHeaders.Add(ClientTokenConstants.ClientAuthTokenCookieName);
                    allowHeaders.Add(OdinHeaderNames.FileSystemTypeHeader);
                }
            }
            else if ( //if the browser gives me an origin that is this identity (i.e. the home or owner app) 
                string.Equals(context.Request.Headers["Origin"], $"https://{odinContext.Tenant.DomainName}", StringComparison.InvariantCultureIgnoreCase) &&
                string.Equals(context.Request.Host.Host, $"{DnsConfigurationSet.PrefixApi}.{odinContext.Tenant.DomainName}", StringComparison.InvariantCultureIgnoreCase))
            {
                context.Response.Headers.Add("Access-Control-Allow-Origin", $"https://{odinContext.Tenant.DomainName}");
                allowHeaders.Add(OdinHeaderNames.FileSystemTypeHeader);
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
