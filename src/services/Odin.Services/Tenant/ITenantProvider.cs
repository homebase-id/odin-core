#nullable enable
namespace Odin.Services.Tenant
{
    public interface ITenantProvider
    {
        Tenant? GetCurrentTenant();
    }
}