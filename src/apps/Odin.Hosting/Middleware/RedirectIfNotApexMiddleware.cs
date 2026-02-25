using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Odin.Services.Authorization.Capi;
using Odin.Services.Tenant;

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

    public Task Invoke(HttpContext context)
    {
        if (!context.Request.IsHttps)
        {
            return _next(context);
        }
        
        if (context.Request.Headers.ContainsKey(ICapiCallbackSession.SessionHttpHeaderName))
        {
            return _next(context);
        }

        if (!HttpMethods.IsGet(context.Request.Method))
        {
            return _next(context);
        }

        var tenant = _tenantProvider.GetCurrentTenant();
        if (tenant == null)
        {
            return _next(context);
        }

        if (tenant.Name == context.Request.Host.Host)
        {
            return _next(context);
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
        _logger.LogTrace("Redirecting to {URL}", redirectUrl);
        context.Response.Redirect(redirectUrl);
        return Task.CompletedTask;
    }
}