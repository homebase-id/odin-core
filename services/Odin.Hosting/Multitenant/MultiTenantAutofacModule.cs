#nullable enable
using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Odin.Core.Services.Tenant;

namespace Odin.Hosting.Multitenant
{
    public class MultiTenantAutofacModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<HttpContextAccessor>().As<IHttpContextAccessor>().SingleInstance();
            builder.RegisterType<TenantProvider>().As<ITenantProvider>().SingleInstance();
        }
    }

    //

    public static class MultiTenantExtensions
    {
        public static IApplicationBuilder UseMultiTenancy(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<MultiTenantContainerMiddleware>();
        }
    }
}
