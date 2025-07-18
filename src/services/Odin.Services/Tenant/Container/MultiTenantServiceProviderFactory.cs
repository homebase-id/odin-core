using System;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Odin.Services.Tenant.Container;

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
        var container = CreateMultiTenantContainer(containerBuilder);
        return new AutofacServiceProvider(container);
    }

    //

    public static MultiTenantContainer CreateMultiTenantContainer(ContainerBuilder containerBuilder)
    {
        MultiTenantContainer container = null!;

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

        return container;
    }

    //
       
}