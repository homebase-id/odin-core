using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Odin.Core.Storage.Cache;

namespace Odin.Services.Certificate;

#nullable enable

public sealed class CertesAcmeMiddleware(RequestDelegate next, ISystemLevel2Cache<CertesAcme> tokenCache)
{
    //

    public async Task Invoke(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Quickly bail if request is not acme-challenge related
        if (!HttpMethods.IsGet(context.Request.Method) || !path.StartsWith("/.well-known/acme-challenge/"))
        {
            await next(context);
            return;
        }

        // Handy for testing connectivity
        if (path == "/.well-known/acme-challenge/ping")
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsync("pong");
            return;
        }

        // Respond to challenge
        var match = Regex.Match(path, @"^\/\.well-known\/acme-challenge\/([a-zA-Z0-9_-]+)");
        if (match.Success)
        {
            var token = match.Groups[1].Value;

            // We cache nulls here to avoid hitting L2 repeatedly for non-existing tokens
            var keyAuth = await tokenCache.GetOrSetAsync(
                token,
                _ => Task.FromResult<string?>(null),
                TimeSpan.FromMinutes(60));

            if (!string.IsNullOrEmpty(keyAuth))
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync(keyAuth);
                return;
            }
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("Not found");
    }

    //

}