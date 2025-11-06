using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Odin.Services.Base;
using Odin.Services.Configuration;

namespace Odin.Hosting.Middleware;

#nullable enable

public class CdnMiddleware(RequestDelegate next, OdinConfiguration config)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        if (config.Cdn.Enabled)
        {
            httpContext.Response.Headers[OdinHeaderNames.OdinCdnPayload] = config.Cdn.PayloadBaseUrl;
        }
        await next(httpContext);
    }
}

