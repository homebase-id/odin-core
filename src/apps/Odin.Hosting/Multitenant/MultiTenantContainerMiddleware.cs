using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

#nullable enable
namespace Odin.Hosting.Multitenant
{
    internal class MultiTenantContainerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<MultiTenantContainerMiddleware> _logger;
        private readonly IMultiTenantContainerAccessor _container;
        private readonly IIdentityRegistry _identityRegistry;

        //

        public MultiTenantContainerMiddleware(
            RequestDelegate next,
            ILogger<MultiTenantContainerMiddleware> logger,
            IMultiTenantContainerAccessor container,
            IIdentityRegistry identityRegistry)
        {
            _next = next;
            _logger = logger;
            _container = container;
            _identityRegistry = identityRegistry;
        }
        
        //

        public async Task Invoke(HttpContext context)
        {
            ILifetimeScope? scope = null;

            // Bail if we don't know the hostname/tenant
            var host = context.Request.Host.Host;
            var registration = _identityRegistry.ResolveIdentityRegistration(host, out _);
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
            try
            {
                scope = _container.Container().GetCurrentTenantScope().BeginLifetimeScope("requestscope");
                context.RequestServices = new AutofacServiceProvider(scope);
                await _next(context);
            }
            finally
            {
                scope?.Dispose();
            }
        }
    }
}
