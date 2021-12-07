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
            var scope = container.ContainerAccessor().GetCurrentTenantScope().BeginLifetimeScope(); 
            context.RequestServices = new AutofacServiceProvider(scope);
            
            await _next(context);
        }
    }
}