using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Odin.Core.Services.Certificate;

#nullable enable

public sealed class CertesAcmeMiddleware
{
    private readonly RequestDelegate _next;

    public CertesAcmeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    //

    public async Task InvokeAsync(HttpContext context, IAcmeHttp01TokenCache cache)
    {
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? "";

        // Quickly bail if request is not acme-challenge related
        if (!HttpMethods.IsGet(method) || !path.StartsWith("/.well-known/acme-challenge/"))
        {
            await _next(context);
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
            if (cache.TryGet(token, out var keyAuth))
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync(keyAuth);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync($"Not found: {token}");
            }
            return;
        }

        // These were not the droids you were looking for...
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("Not found");
    }

    //

}