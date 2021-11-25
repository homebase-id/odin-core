using System.Threading.Tasks;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;

#nullable enable
namespace Youverse.Hosting.Multitenant
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

        public async Task Invoke(HttpContext context, MultiTenantContainerDisposableAccessor container)
        {
            // Begin new scope for request as ASP.NET Core standard scope is per-request
            context.RequestServices = new AutofacServiceProvider(
                container.ContainerAccessor().GetCurrentTenantScope().BeginLifetimeScope());
            
            await _next(context);
        }
    }
}