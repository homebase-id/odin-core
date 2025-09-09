using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Odin.Services.Base;
using Odin.Services.LastSeen;

namespace Odin.Hosting.Middleware;

#nullable enable

public class LastSeenMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        OdinContext odinContext,
        ILastSeenService lastSeen)
    {
        if (odinContext.Caller.OdinId.HasValue)
        {
            await lastSeen.LastSeenNowAsync(odinContext.Caller.OdinId);
        }
        await next(httpContext);
    }
}

