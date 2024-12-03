using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;

namespace Odin.Hosting.Middleware;

public static class HttpsPortRedirectionExtensions
{
    public static IApplicationBuilder UseHttpsPortRedirection(this IApplicationBuilder app, int httpsPort)
    {
        return app.UseMiddleware<HttpsPortRedirectMiddleware>(httpsPort);
    }
}

/// <summary>
/// Middleware that redirects non-HTTPS requests to an HTTPS URL.
/// </summary>
public class HttpsPortRedirectMiddleware
{
    private readonly ILogger<HttpsPortRedirectMiddleware> _logger;
    private readonly RequestDelegate _next;

    private readonly int _httpsPort;

    //

    public HttpsPortRedirectMiddleware(ILogger<HttpsPortRedirectMiddleware> logger, RequestDelegate next, int httpsPort)
    {
        _logger = logger;
        _next = next;
        _httpsPort = httpsPort;
    }

    //

    public Task Invoke(HttpContext context)
    {
        if (context.Request.IsHttps)
        {
            return _next(context);
        }

        var host = context.Request.Host;
        if (_httpsPort != 443)
        {
            host = new HostString(host.Host, _httpsPort);
        }
        else
        {
            host = new HostString(host.Host);
        }

        var request = context.Request;
        var redirectUrl = UriHelper.BuildAbsolute(
            "https",
            host,
            request.PathBase,
            request.Path,
            request.QueryString);

        context.Response.Redirect(redirectUrl);
        return Task.CompletedTask;
    }
}

