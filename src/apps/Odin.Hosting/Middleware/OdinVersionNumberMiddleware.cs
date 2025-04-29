using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Odin.Core.Exceptions;
using Odin.Services.Base;

namespace Odin.Hosting.Middleware;

public class OdinVersionNumberMiddleware(RequestDelegate next)
{
    /// <summary/>
    public async Task Invoke(HttpContext httpContext)
    {
        httpContext.Response.Headers[OdinHeaderNames.OdinVersionTag] = Odin.Services.Version.VersionText;
        await next(httpContext);
    }
}

