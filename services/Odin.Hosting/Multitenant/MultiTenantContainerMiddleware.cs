#nullable enable
using System.Threading.Tasks;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Odin.Core.Services.Registry;

namespace Odin.Hosting.Multitenant
{
    internal class MultiTenantContainerMiddleware
    {
        private readonly RequestDelegate _next;
        
        //

        public MultiTenantContainerMiddleware(RequestDelegate next)
        {
            _next = next;
        }
        
        //

        public async Task Invoke(
            HttpContext context,
            ILogger<MultiTenantContainerMiddleware> logger,
            MultiTenantContainerDisposableAccessor container,
            IIdentityRegistry identityRegistry)
        {
            // Bail if we don't know the hostname/tenant
            var host = context.Request.Host.Host;
            var registry = identityRegistry.ResolveIdentityRegistration(host, out _);
            if (registry == null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync($"{host} not found.");
                return;
            }
            
            // Begin new scope for request as ASP.NET Core standard scope is per-request
            var scope = container.ContainerAccessor().GetCurrentTenantScope().BeginLifetimeScope("requestscope"); 
            context.RequestServices = new AutofacServiceProvider(scope);
            
            await _next(context);
        }
    }
}
