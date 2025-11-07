using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;

namespace Odin.Hosting.Middleware;

public class StaticFileCachingMiddleware(RequestDelegate next, IHostEnvironment env)
{
    private static readonly StringValues MaxAgeOneYear = new("max-age=31536000");
    private static readonly string[] Paths =
    [
        "/assets",
        "/emoji",
        "/icons",
        "/owner/assets",
        "/owner/icons"
    ];

    private readonly bool _isDevelopment = env.IsDevelopment();

    public Task Invoke(HttpContext httpContext)
    {
        if (_isDevelopment)
        {
            return next(httpContext);
        }

        var path = httpContext.Request.Path;
        if (Paths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            httpContext.Response.Headers.CacheControl = MaxAgeOneYear;
        }

        return next(httpContext);
    }
}