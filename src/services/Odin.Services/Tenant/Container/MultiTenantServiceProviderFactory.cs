using System;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Configuration;

#nullable enable
namespace Odin.Services.Tenant.Container
{
    public class MultiTenantServiceProviderFactory : IServiceProviderFactory<ContainerBuilder>
    {
        private readonly Action<ContainerBuilder, Tenant, OdinConfiguration> _tenantServicesConfiguration;
        private readonly Action<ILifetimeScope, Tenant> _tenantInitialization;
        private readonly OdinConfiguration _config;

        public MultiTenantServiceProviderFactory(
            Action<ContainerBuilder, Tenant, OdinConfiguration> tenantServicesConfiguration,
            Action<ILifetimeScope, Tenant> tenantInitialization,
            OdinConfiguration config)
        {
            _tenantServicesConfiguration = tenantServicesConfiguration;
            _tenantInitialization = tenantInitialization;
            _config = config;
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
                .RegisterInstance(new MultiTenantContainerAccessor(ContainerAccessor))
                .As<IMultiTenantContainerAccessor>()
                .SingleInstance();
            
            container = new MultiTenantContainer(
                containerBuilder.Build(), 
                _tenantServicesConfiguration,
                _tenantInitialization,
                _config);

            return new AutofacServiceProvider(container);
        }
       
    }
}
