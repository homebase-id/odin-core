using System;

#nullable enable
namespace Odin.Services.Tenant.Container
{
    public interface IMultiTenantContainerAccessor : IDisposable
    {
        Func<MultiTenantContainer> Container { get; }
    }

    //

    public sealed class MultiTenantContainerAccessor : IMultiTenantContainerAccessor
    {
        public Func<MultiTenantContainer> Container { get; }

        //
        
        public MultiTenantContainerAccessor(Func<MultiTenantContainer> container)
        {
            Container = container;
        }

        //
        
        public void Dispose()
        {
            var container = Container();
            container.Dispose();    
        }
    }
}