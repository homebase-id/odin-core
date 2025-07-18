using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

#nullable enable
namespace Odin.Hosting.Multitenant;

internal class MultiTenantContainerMiddleware(
    RequestDelegate next,
    IMultiTenantContainer container,
    IIdentityRegistry identityRegistry,
    ICorrelationContext correlationContext)
{
    public async Task Invoke(HttpContext context)
    {
        // Bail if we don't know the hostname/tenant
        var host = context.Request.Host.Host;
        var registration = identityRegistry.ResolveIdentityRegistration(host, out _);
        if (registration == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync($"{host} not found");
            return;
        }

        if (registration.Disabled)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsync($"{host} is disabled");
            return;
        }

        // Begin new scope for the request (this is where e.g. OdinContext is created)
        ILifetimeScope? requestScope = null;
        try
        {
            var tenantScope = container.GetTenantScope(registration.PrimaryDomainName);
            requestScope = tenantScope.BeginLifetimeScope($"Request:{correlationContext.Id}");
            context.RequestServices = new AutofacServiceProvider(requestScope);
            await next(context);
        }
        finally
        {
            requestScope?.Dispose();
        }
    }
}
