using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Youverse.Hosting.Middleware
{
    public class StaticFileCachingMiddleware
    {
        private readonly RequestDelegate _next;

        private static readonly List<string> paths = new List<string>()
        {
            "/home/static/js",
            "/owner/static/js"
        };
        
        public StaticFileCachingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (paths.Any(s => httpContext.Request.Path.StartsWithSegments(s)))
            {
                httpContext.Response.Headers.Add("Cache-Control", "max-age=31536000");
            }

            await _next(httpContext);
        }
    }
}