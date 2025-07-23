using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Autofac;
using Autofac.Core;
using Autofac.Core.Lifetime;
using Autofac.Core.Resolving;
using Odin.Core.Exceptions;

#nullable enable
namespace Odin.Services.Tenant.Container;

public interface IMultiTenantContainer : IContainer
{
    ILifetimeScope GetOrAddTenantScope(string domain, Action<ContainerBuilder> configurationAction);
    ILifetimeScope GetTenantScope(string domain);
    ILifetimeScope? LookupTenantScope(string domain);
    void RemoveTenantScope(string domain);
    List<ILifetimeScope> GetTenantScopesForDiagnostics();
}

//

public sealed class MultiTenantContainer(IContainer applicationContainer) : IMultiTenantContainer
{
    // This is the base application container

    // This dictionary keeps track of all of the tenant scopes that we have created
    private readonly ConcurrentDictionary<string, Lazy<ILifetimeScope>> _tenantLifetimeScopes = new();
    private bool _disposed;

    //

    /// <summary>
    /// Get/create the scope of a domain
    /// </summary>
    /// <returns>ILifetimeScope</returns>
    public ILifetimeScope GetTenantScope(string domain)
    {
        return LookupTenantScope(domain) ?? throw new OdinSystemException($"Tenant scope not found for {domain}");
    }

    /// <summary>
    /// Look up the scope of a domain, if it exits
    /// </summary>
    /// <returns>ILifetimeScope?</returns>
    public ILifetimeScope? LookupTenantScope(string domain)
    {
        return _tenantLifetimeScopes.TryGetValue(domain, out var lazyScope) ? lazyScope.Value : null;
    }

    //

    /// <summary>
    /// Remove scope of domain
    /// </summary>
    /// <returns></returns>
    public void RemoveTenantScope(string domain)
    {
        if (_tenantLifetimeScopes.TryRemove(domain, out var scope))
        {
            scope.Value.Dispose();
        }
    }

    //

    /// <summary>
    /// Get or create a tenant scope. The tenant doesn't have to exist.
    /// </summary>
    /// <param name="domain"></param>
    /// <param name="configurationAction"></param>
    /// <returns></returns>
    public ILifetimeScope GetOrAddTenantScope(string domain, Action<ContainerBuilder> configurationAction)
    {
        if (_disposed)
        {
            return applicationContainer;
        }

        // SEB:NOTE
        // The valueFactory is not run under lock, so we use Lazy<> to make sure that it is only created once
        var lazyScope = _tenantLifetimeScopes.GetOrAdd(domain, _ => new Lazy<ILifetimeScope>(() =>
            applicationContainer.BeginLifetimeScope(domain, configurationAction)));

        return lazyScope.Value;
    }

    //

    // Only for diagnostics
    public List<ILifetimeScope> GetTenantScopesForDiagnostics()
    {
        return _tenantLifetimeScopes.Values.Select(x => x.Value).ToList();
    }

    //

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            var keys = _tenantLifetimeScopes.Keys.ToArray();
            foreach (var key in keys)
            {
                if (_tenantLifetimeScopes.TryRemove(key, out var scope))
                {
                    scope.Value.Dispose();
                }
            }
            applicationContainer.Dispose(); // Do it! We own it!
        }
    }

    //

    public ValueTask DisposeAsync()
    {
        Dispose();
        return new ValueTask();
    }

    //

    public object ResolveComponent(in ResolveRequest request) =>
        applicationContainer.ResolveComponent(request);

    public IComponentRegistry ComponentRegistry =>
        applicationContainer.ComponentRegistry;

    public ILifetimeScope BeginLifetimeScope() =>
        applicationContainer.BeginLifetimeScope();

    public ILifetimeScope BeginLifetimeScope(object tag) =>
        applicationContainer.BeginLifetimeScope(tag);

    public ILifetimeScope BeginLifetimeScope(Action<ContainerBuilder> configurationAction) =>
        applicationContainer.BeginLifetimeScope(configurationAction);

    public ILifetimeScope BeginLifetimeScope(object tag, Action<ContainerBuilder> configurationAction) =>
        applicationContainer.BeginLifetimeScope(tag, configurationAction);

    public ILifetimeScope BeginLoadContextLifetimeScope(AssemblyLoadContext loadContext, Action<ContainerBuilder> configurationAction) =>
        applicationContainer.BeginLoadContextLifetimeScope(loadContext, configurationAction);

    public ILifetimeScope BeginLoadContextLifetimeScope(object tag, AssemblyLoadContext loadContext, Action<ContainerBuilder> configurationAction) =>
        applicationContainer.BeginLoadContextLifetimeScope(tag, loadContext, configurationAction);

    public IDisposer Disposer =>
        applicationContainer.Disposer;

    public object Tag =>
        applicationContainer.Tag;

    public event EventHandler<LifetimeScopeBeginningEventArgs> ChildLifetimeScopeBeginning { add{} remove{} }
    public event EventHandler<LifetimeScopeEndingEventArgs> CurrentScopeEnding { add{} remove{} }
    public event EventHandler<ResolveOperationBeginningEventArgs> ResolveOperationBeginning { add{} remove{} }

    public DiagnosticListener DiagnosticSource => new ("MultiTenantContainer");
}
