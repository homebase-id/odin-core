using System;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Services.Tenant;

#nullable enable
namespace Youverse.Hosting.Multitenant
{
    public class MultiTenantServiceProviderFactory : IServiceProviderFactory<ContainerBuilder>
    {
        private readonly Action<ContainerBuilder, Tenant> _tenantServicesConfiguration;

        public MultiTenantServiceProviderFactory(Action<ContainerBuilder, Tenant> tenantServicesConfiguration)
        {
            _tenantServicesConfiguration = tenantServicesConfiguration;
        }
        
        //

        public ContainerBuilder CreateBuilder(IServiceCollection services)
        {
            var builder = new ContainerBuilder();
            builder.Populate(services);
            return builder;
        }
        
        //

        public IServiceProvider CreateServiceProvider(ContainerBuilder containerBuilder)
        {
            MultiTenantContainer container = default!;
            
            MultiTenantContainer ContainerAccessor()
            {
                return container!;
            }
            
            containerBuilder
                .RegisterInstance(new MultiTenantContainerDisposableAccessor(ContainerAccessor))
                .SingleInstance();
            
            container = new MultiTenantContainer(containerBuilder.Build(), _tenantServicesConfiguration);

            return new AutofacServiceProvider(container);
        }
       
    }
}
