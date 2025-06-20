using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Odin.Services.Certificate;

#nullable enable

public sealed class CertesAcmeMiddleware(RequestDelegate next, IAcmeHttp01TokenCache cache)
{
    //

    public Task Invoke(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Quickly bail if request is not acme-challenge related
        if (!HttpMethods.IsGet(context.Request.Method) || !path.StartsWith("/.well-known/acme-challenge/"))
        {
            return next(context);
        }

        // Handy for testing connectivity
        if (path == "/.well-known/acme-challenge/ping")
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return context.Response.WriteAsync("pong");
        }

        // Respond to challenge
        var match = Regex.Match(path, @"^\/\.well-known\/acme-challenge\/([a-zA-Z0-9_-]+)");
        if (match.Success)
        {
            var token = match.Groups[1].Value;
            if (cache.TryGet(token, out var keyAuth))
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return context.Response.WriteAsync(keyAuth);
            }
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return context.Response.WriteAsync("Not found");
    }

    //

}