using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Odin.Hosting.Middleware;

#nullable enable

public class HealthzMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        // k3s queries this endpoint to check if the app is alive.
        // If it returns a non-200 status code, k3s will restart the container.
        if (httpContext.Request.Path == "/healthz")
        {
            httpContext.Response.StatusCode = 200;
            await httpContext.Response.WriteAsync("Healthy");
            return;
        }
        await next(httpContext);
    }
}

