#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using Autofac.Core;
using Autofac.Core.Lifetime;
using Autofac.Core.Resolving;
using Odin.Core.Services.Tenant;

namespace Odin.Hosting.Multitenant
{
    public class MultiTenantContainer : IContainer
    {
        //This is the base application container
        private readonly  IContainer _applicationContainer;

        //This action configures a container builder
        private readonly Action<ContainerBuilder, Tenant> _tenantServiceConfiguration;
        private readonly Action<ILifetimeScope, Tenant> _tenantInitialization;
        
        //This dictionary keeps track of all of the tenant scopes that we have created
        private readonly Dictionary<string, ILifetimeScope> _tenantLifetimeScopes = new();
        
        private readonly object _lock = new();
        private const string MultiTenantTag = "multitenantcontainer";

        public MultiTenantContainer(
            IContainer applicationContainer, 
            Action<ContainerBuilder, Tenant> serviceConfiguration,
            Action<ILifetimeScope, Tenant> tenantInitialization
            )
        {
            _applicationContainer = applicationContainer;
            _tenantServiceConfiguration = serviceConfiguration;
            _tenantInitialization = tenantInitialization;
        }

        /// <summary>
        /// Get the current teanant from the application container
        /// </summary>
        /// <returns></returns>
        private Tenant? GetCurrentTenant()
        {
            return  _applicationContainer.Resolve<ITenantProvider>().GetCurrentTenant();
        }
        
        /// <summary>
        /// Get the scope of the current tenant
        /// </summary>
        /// <returns></returns>
        public ILifetimeScope GetCurrentTenantScope()
        {
            var tenant = GetCurrentTenant();
            return GetTenantScope(tenant?.Name);
        }

        /// <summary>
        /// Get (configure on missing)
        /// </summary>
        /// <param name="tenantId"></param>
        /// <returns></returns>
        private ILifetimeScope GetTenantScope(string? tenantId)
        {
            //If no tenant (e.g. early on in the pipeline, we just use the application container)
            if (tenantId == null)
            {
                return _applicationContainer;
            }

            if (_tenantLifetimeScopes.ContainsKey(tenantId))
            {
                return _tenantLifetimeScopes[tenantId];
            }

            Tenant? tenant;
            ILifetimeScope lifetimeScope;
            lock (_lock) // SEB:TODO swap this for a ReaderWriterLockSlim 
            {
                if (_tenantLifetimeScopes.ContainsKey(tenantId))
                {
                    return _tenantLifetimeScopes[tenantId];
                }
                
                tenant = GetCurrentTenant();
                if (tenant == null) // sanity
                {
                    return _applicationContainer; 
                }

                // This is a new tenant, configure a new lifetimescope for it using our tenant sensitive configuration method
                lifetimeScope = _applicationContainer.BeginLifetimeScope(
                    MultiTenantTag,
                    cb => _tenantServiceConfiguration(cb, tenant));
                
                _tenantLifetimeScopes.Add(tenantId, lifetimeScope); 
            }

            _tenantInitialization(lifetimeScope, tenant);    
            
            return _tenantLifetimeScopes[tenantId];
        }

        public void Dispose()
        {
            lock (_lock) // SEB:TODO swap this for a ReaderWriterLockSlim
            {
                foreach (var scope in _tenantLifetimeScopes)
                {
                    scope.Value.Dispose();
                }
                _tenantLifetimeScopes.Clear();
                _applicationContainer.Dispose(); // SEB:TODO really? _applicationContainer is injected
            }
            GC.SuppressFinalize(this);
        }

        public object ResolveComponent(ResolveRequest request) => 
            GetCurrentTenantScope().ResolveComponent(request);
        
        public IComponentRegistry ComponentRegistry => 
            GetCurrentTenantScope().ComponentRegistry;
        
        public ValueTask DisposeAsync() => 
            GetCurrentTenantScope().DisposeAsync();
        
        public ILifetimeScope BeginLifetimeScope() => 
            GetCurrentTenantScope().BeginLifetimeScope();
        
        public ILifetimeScope BeginLifetimeScope(object tag) => 
            GetCurrentTenantScope().BeginLifetimeScope(tag);
        
        public ILifetimeScope BeginLifetimeScope(Action<ContainerBuilder> configurationAction) => 
            GetCurrentTenantScope().BeginLifetimeScope(configurationAction);

        public ILifetimeScope BeginLifetimeScope(object tag, Action<ContainerBuilder> configurationAction) =>
            GetCurrentTenantScope().BeginLifetimeScope(tag, configurationAction);

        public IDisposer Disposer =>
            GetCurrentTenantScope().Disposer;
        
        public object Tag => 
            GetCurrentTenantScope().Tag;
        
        public event EventHandler<LifetimeScopeBeginningEventArgs> ChildLifetimeScopeBeginning { add{} remove{} }
        public event EventHandler<LifetimeScopeEndingEventArgs> CurrentScopeEnding { add{} remove{} }
        public event EventHandler<ResolveOperationBeginningEventArgs> ResolveOperationBeginning { add{} remove{} }
        
        public DiagnosticListener DiagnosticSource => new ("MultiTenantContainer");
    }
}