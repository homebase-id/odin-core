using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Odin.Hosting.Middleware
{
    public class StaticFileCachingMiddleware(RequestDelegate next)
    {
        //Note: be sure it does not end with a "/"
        private static readonly List<string> paths = new List<string>()
        {
            "/assets",
            "/owner/assets",
            "/assets",
            "/owner/assets",
            "/owner/icons",
            "/icons",
            "/emoji",
        };

        public async Task Invoke(HttpContext httpContext, IHostEnvironment env)
        {
            var isDev = env.IsDevelopment();

            if (paths.Any(s => httpContext.Request.Path.StartsWithSegments(s)) && !isDev)
            {
                httpContext.Response.Headers.Append("Cache-Control", "max-age=31536000");
            }

            await next(httpContext);
        }
    }
}
