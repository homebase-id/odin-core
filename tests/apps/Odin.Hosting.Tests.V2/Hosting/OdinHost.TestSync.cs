#nullable enable
using System;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Tests.V2.Hosting;

public sealed partial class OdinHost
{
    /// <summary>
    /// Resolves <see cref="ITestSync"/> from <paramref name="domain"/>'s tenant scope. The impl is
    /// registered at root container level by <see cref="StartAsync"/>; tenant scopes pick it up
    /// via parent-scope fallback. Production never registers an impl.
    /// </summary>
    public ITestSync GetTestSync(string domain)
    {
        var multitenant = _host.Services.GetRequiredService<IMultiTenantContainer>();
        var scope = multitenant.LookupTenantScope(domain)
            ?? throw new InvalidOperationException(
                $"No tenant scope for {domain} — call EnsureTenantsMaterializedAsync first.");
        return scope.Resolve<ITestSync>();
    }
}
