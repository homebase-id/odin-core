using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#nullable enable
namespace Youverse.Hosting.Multitenant
{
    public static class MultiTenantExtensions
    {
        public static IServiceCollection AddMultiTenancy(this IServiceCollection services)
        {
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddSingleton<ITenantProvider, TenantProvider>();
            services.AddSingleton<ITenantResolutionStrategy, HostResolutionStrategy>();
            return services;
        }
        
        public static IApplicationBuilder UseMultiTenancy(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<MultiTenantContainerMiddleware>();
        }
    }
}
