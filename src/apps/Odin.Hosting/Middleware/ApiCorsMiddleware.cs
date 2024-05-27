using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Odin.Services.Base;
using Odin.Services.Registry.Registration;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Services.Configuration;

namespace Odin.Hosting.Middleware
{
    public class ApiCorsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly OdinConfiguration _config;

        public ApiCorsMiddleware(RequestDelegate next, OdinConfiguration config)
        {
            _next = next;
            _config = config;
        }

        public Task Invoke(HttpContext context, IOdinContext odinContext)
        {
            if (context.Request.Method == "OPTIONS")
            {
                //handled by a controller
                return _next.Invoke(context);
            }

            bool shouldSetHeaders = false;

            List<string> allowHeaders = new List<string>();

            if (odinContext.AuthContext == YouAuthConstants.AppSchemeName)
            {
                string appHostName = odinContext.Caller.OdinClientContext.CorsHostName;
                if (!string.IsNullOrEmpty(appHostName))
                {
                    shouldSetHeaders = true;
                    context.Response.Headers.Append(
                        "Access-Control-Allow-Origin", $"https://{appHostName}:{_config.Host.DefaultHttpsPort}");
                    allowHeaders.Add(YouAuthConstants.AppCookieName);
                    allowHeaders.Add(OdinHeaderNames.FileSystemTypeHeader);
                }
            }

            if (shouldSetHeaders)
            {
                context.Response.Headers.Append("Access-Control-Allow-Credentials", new[] { "true" });
                context.Response.Headers.Append("Access-Control-Allow-Headers", allowHeaders.ToArray());
                context.Response.Headers.Append("Access-Control-Expose-Headers",
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
