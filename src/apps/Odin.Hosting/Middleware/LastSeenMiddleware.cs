using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Odin.Core.Identity;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.LastSeen;

namespace Odin.Hosting.Middleware;

public class LastSeenMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        OdinIdentity odinIdentity,
        OdinContext odinContext,
        ILastSeenService lastSeen)
    {
        if (odinContext.Caller.IsOwner && odinContext.AuthContext is YouAuthConstants.AppSchemeName or OwnerAuthConstants.SchemeName)
        {
            await lastSeen.LastSeenNowAsync(odinIdentity);
        }
        await next(httpContext);
    }
}

