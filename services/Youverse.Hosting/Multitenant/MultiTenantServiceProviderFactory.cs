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
        private readonly Action<ILifetimeScope, Tenant> _tenantInitialization;

        public MultiTenantServiceProviderFactory(
            Action<ContainerBuilder, Tenant> tenantServicesConfiguration,
            Action<ILifetimeScope, Tenant> tenantInitialization)
        {
            _tenantServicesConfiguration = tenantServicesConfiguration;
            _tenantInitialization = tenantInitialization;
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
            
            container = new MultiTenantContainer(
                containerBuilder.Build(), 
                _tenantServicesConfiguration,
                _tenantInitialization);

            return new AutofacServiceProvider(container);
        }
       
    }
}
