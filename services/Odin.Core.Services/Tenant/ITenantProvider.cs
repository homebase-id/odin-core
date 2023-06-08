#nullable enable
namespace Odin.Core.Services.Tenant
{
    public interface ITenantProvider
    {
        Tenant? GetCurrentTenant();
    }
}