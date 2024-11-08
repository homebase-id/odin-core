using System;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Odin.Services.Tenant.Container
{
    public class MultiTenantServiceProviderFactory : IServiceProviderFactory<ContainerBuilder>
    {
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
                // ReSharper disable once AccessToModifiedClosure
                return container!;
            }
            
            containerBuilder
                .RegisterInstance(new MultiTenantContainerAccessor(ContainerAccessor))
                .As<IMultiTenantContainerAccessor>()
                .SingleInstance();
            
            container = new MultiTenantContainer(containerBuilder.Build());

            return new AutofacServiceProvider(container);
        }
       
    }
}
