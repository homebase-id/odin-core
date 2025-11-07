using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Odin.Services.Base;

namespace Odin.Hosting.Middleware;

public class OdinVersionNumberMiddleware(RequestDelegate next)
{
    /// <summary/>
    public Task InvokeAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers[OdinHeaderNames.OdinVersionTag] = Services.Version.VersionText;
        return next(httpContext);
    }
}

