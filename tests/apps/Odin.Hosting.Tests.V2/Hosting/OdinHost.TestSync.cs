#nullable enable
using System;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Background.Testing;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Tests.V2.Hosting;

public sealed partial class OdinHost
{
    /// <summary>
    /// Resolves <see cref="ITestSync"/> from <paramref name="domain"/>'s tenant scope. Available
    /// because <c>Testing__EnableSyncHooks</c> is set in the global env baseline; in production
    /// the binding doesn't exist and this would throw.
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
