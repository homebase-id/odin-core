using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Odin.Core.Services.Tenant;

namespace Odin.Hosting.Middleware;

public class RedirectIfNotApexMiddleware
{
    private readonly ILogger<RedirectIfNotApexMiddleware> _logger;
    private readonly RequestDelegate _next;
    private readonly ITenantProvider _tenantProvider;

    //

    public RedirectIfNotApexMiddleware(
        ILogger<RedirectIfNotApexMiddleware> logger,
        RequestDelegate next,
        ITenantProvider tenantProvider)
    {
        _next = next;
        _tenantProvider = tenantProvider;
        _logger = logger;
    }

    //

    public async Task Invoke(HttpContext context)
    {
        if (!context.Request.IsHttps)
        {
            await _next(context);
            return;
        }

        if (context.Connection.ClientCertificate != null)
        {
            await _next(context);
            return;
        }

        if (context.Request.Method != "GET")
        {
            await _next(context);
            return;
        }

        var tenant = _tenantProvider.GetCurrentTenant();
        if (tenant == null)
        {
            await _next(context);
            return;
        }

        if (tenant.Name == context.Request.Host.Host)
        {
            await _next(context);
            return;
        }

        // Redirect to identity apex
        var uriBuilder = new UriBuilder
        {
            Scheme = context.Request.Scheme,
            Host = tenant.Name,
            Port = context.Request.Host.Port == 443 ? -1 : context.Request.Host.Port ?? -1,
            Path = context.Request.Path,
            Query = context.Request.QueryString.ToString()
        };
        var redirectUrl = uriBuilder.ToString();
        _logger.LogDebug("Redirecting to {URL}", redirectUrl);
        context.Response.Redirect(redirectUrl);
    }
}