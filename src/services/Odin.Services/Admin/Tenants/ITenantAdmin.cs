using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Services.Admin.Tenants;
#nullable enable

public interface ITenantAdmin
{
    Task<List<TenantModel>> GetTenants(bool includePayload);
    Task<TenantModel?> GetTenant(string domain, bool includePayload);
    Task<bool> TenantExists(string domain);

    Task EnableTenant(string domain);
    Task DisableTenant(string domain);

    Task<string> EnqueueDeleteTenant(string domain);
    Task<string> OldEnqueueExportTenant(string domain);
}
