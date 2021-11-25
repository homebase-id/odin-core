using Youverse.Core.Services.Tenant;

#nullable enable
namespace Youverse.Hosting.Multitenant
{
    public interface ITenantProvider
    {
        Tenant? GetCurrentTenant();
    }
}