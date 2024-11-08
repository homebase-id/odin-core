using System;
using Autofac;

#nullable enable
namespace Odin.Services.Tenant.Container
{
    public interface IMultiTenantContainerAccessor : IDisposable
    {
        Func<MultiTenantContainer> Container { get; }
        ILifetimeScope GetOrAddTenantScope(string tenant, Action<ContainerBuilder> configurationAction);
        ILifetimeScope GetTenantScope(string tenant);
        ILifetimeScope? LookupTenantScope(string tenant);
    }

    //

    public sealed class MultiTenantContainerAccessor(Func<MultiTenantContainer> container)
        : IMultiTenantContainerAccessor
    {
        public Func<MultiTenantContainer> Container { get; } = container;

        public ILifetimeScope GetOrAddTenantScope(string tenant, Action<ContainerBuilder> configurationAction) =>
            Container().GetOrAddTenantScope(tenant, configurationAction);
        public ILifetimeScope GetTenantScope(string tenant) => Container().GetTenantScope(tenant);
        public ILifetimeScope? LookupTenantScope(string tenant) => Container().LookupTenantScope(tenant);

        public void Dispose()
        {
            var container = Container();
            container.Dispose();    
        }
    }
}