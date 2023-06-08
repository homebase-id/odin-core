#nullable enable
namespace Youverse.Core.Services.Tenant
{
    public interface ITenantProvider
    {
        Tenant? GetCurrentTenant();
    }
}