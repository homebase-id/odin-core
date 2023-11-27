using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Odin.Core.Services.Base;
using Odin.Core.Services.Registry.Registration;
using Odin.Hosting.Authentication.YouAuth;

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
                    context.Response.Headers.Append("Access-Control-Allow-Origin", $"https://{appHostName}");
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
