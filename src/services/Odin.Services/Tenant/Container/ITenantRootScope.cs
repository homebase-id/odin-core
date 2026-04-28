#nullable enable
using Autofac;
using Odin.Core.Identity;
namespace Odin.Services.Tenant.Container;

public interface ITenantRootScope
{
    ILifetimeScope? BeginLifetimeScope();
}

public class TenantRootScope(IMultiTenantContainer container, OdinIdentity identity) : ITenantRootScope
{
    public ILifetimeScope? BeginLifetimeScope()
    {
        var scope = container.LookupTenantScope(identity.PrimaryDomain);
        return scope?.BeginLifetimeScope();
    }
}