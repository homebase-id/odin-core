using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Odin.Services.Base;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Configuration;

namespace Odin.Hosting.Middleware
{
    public class ApiCorsMiddleware(RequestDelegate next, OdinConfiguration config)
    {
        private readonly OdinConfiguration _config = config;

        public Task Invoke(HttpContext context, IOdinContext odinContext)
        {
            if (context.Request.Method == "OPTIONS")
            {
                //handled by a controller
                return next.Invoke(context);
            }

            bool shouldSetHeaders = false;

            List<string> allowHeaders = new List<string>();

            if (odinContext.AuthContext == YouAuthConstants.AppSchemeName || odinContext.Caller.ClientTokenType == ClientTokenType.App)
            {
                var appHostName = odinContext.Caller.OdinClientContext.CorsHostName;
                if (!string.IsNullOrEmpty(appHostName))
                {
                    if (context.Request.Host.Port.HasValue)
                    {
                        appHostName += $":{context.Request.Host.Port}";
                    }
                    shouldSetHeaders = true;
                    context.Response.Headers.Append(
                        "Access-Control-Allow-Origin", $"https://{appHostName}");
                    allowHeaders.Add(YouAuthConstants.AppCookieName);
                    allowHeaders.Add(OdinHeaderNames.FileSystemTypeHeader);
                    allowHeaders.Add(OdinHeaderNames.RequiresUpgrade);
                    allowHeaders.Add(OdinHeaderNames.UpgradeIsRunning);
                }
            }
            
            if (shouldSetHeaders)
            {
                context.Response.Headers.Append("Access-Control-Allow-Credentials", new[] { "true" });
                context.Response.Headers.Append("Access-Control-Allow-Headers", allowHeaders.ToArray());
                context.Response.Headers.Append("Access-Control-Expose-Headers",
                    new[]
                    {
                        HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, HttpHeaderConstants.PayloadEncrypted, HttpHeaderConstants.DecryptedContentType
                    });
            }

            return next.Invoke(context);
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
