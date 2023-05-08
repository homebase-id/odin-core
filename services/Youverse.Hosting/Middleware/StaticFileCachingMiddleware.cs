using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Youverse.Hosting.Middleware
{
    public class StaticFileCachingMiddleware
    {
        private readonly RequestDelegate _next;

        //Note: be sure it does not end with a "/"
        private static readonly List<string> paths = new List<string>()
        {
            "/home/assets/js",
            "/owner/assets/js",
            "/home/assets/css",
            "/owner/assets/css",
            "/owner/icons",
            "/home/icons",
        };

        public StaticFileCachingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            var isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == Environments.Development;

            if (paths.Any(s => httpContext.Request.Path.StartsWithSegments(s)) && !isDev)
            {
                httpContext.Response.Headers.Add("Cache-Control", "max-age=31536000");
            }

            await _next(httpContext);
        }
    }
}
