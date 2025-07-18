using System;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Util;

#nullable enable

// Helper class providing DI root in IServiceProvider and Autofac ILifetimeScope with multi-tenant support.
// This is useful in command line applications and test code. Don't use this in the web application.
public sealed class ServiceProviders : IDisposable
{
    public IServiceProvider ServiceProvider { get; }
    public MultiTenantContainer MultiTenantContainer { get; }

    public ServiceProviders(Action<IServiceCollection> serviceCollection, Action<ContainerBuilder> containerBuilder)
    {
        var sc = new ServiceCollection();
        serviceCollection(sc);
        var cb = new ContainerBuilder();
        containerBuilder(cb);
        cb.Populate(sc);
        MultiTenantContainer = MultiTenantServiceProviderFactory.CreateMultiTenantContainer(cb);
        ServiceProvider = MultiTenantContainer.Resolve<IServiceProvider>();
    }

    //

    public static ServiceProviders Create(Action<IServiceCollection> serviceCollection, Action<ContainerBuilder> containerBuilder)
    {
        return new ServiceProviders(serviceCollection, containerBuilder);
    }

    //

    public void Dispose()
    {
        MultiTenantContainer.Dispose();
    }
}
