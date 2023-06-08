#nullable enable
using System;

namespace Odin.Hosting.Multitenant
{
    internal class MultiTenantContainerDisposableAccessor : IDisposable
    {
        public Func<MultiTenantContainer> ContainerAccessor { get; }

        //
        
        public MultiTenantContainerDisposableAccessor(Func<MultiTenantContainer> containerAccessor)
        {
            ContainerAccessor = containerAccessor;
        }

        //
        
        public void Dispose()
        {
            var container = ContainerAccessor(); 
            container.Dispose();    
        }
    }
}