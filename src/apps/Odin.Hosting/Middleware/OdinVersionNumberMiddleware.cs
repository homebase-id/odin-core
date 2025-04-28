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

public class TestVersionHeaderValidatorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(OdinHeaderNames.OdinVersionTag))
            {
                throw new OdinSystemException("Missing required X-Version header in response.");
            }

            return Task.CompletedTask;
        });

        await next(context);
    }
}