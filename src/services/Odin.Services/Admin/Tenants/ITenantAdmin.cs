using System.Collections.Generic;
using Odin.Services.Registry;
using System.Threading.Tasks;

namespace Odin.Services.Admin.Tenants;
#nullable enable

public interface ITenantAdmin
{
    Task<List<TenantModel>> GetTenants(bool includePayload);
    Task<TenantModel?> GetTenantAsync(string domain, bool includePayload);
    Task<bool> TenantExists(string domain);

    /// <summary>
    /// Storage and activity figures for every identity that has data on this node, including
    /// identities with no registration row.
    /// </summary>
    Task<TenantMetricsResponse> GetTenantMetricsAsync();

    Task EnableTenant(string domain);
    Task DisableTenant(string domain);

    /// <summary>
    /// Sets the tenant's status. Returns the previous status, or null if the tenant does not exist.
    /// </summary>
    Task<TenantStatusModel?> SetTenantStatusAsync(string domain, TenantStatus status, DisabledReason? reason);

    Task EnablePublicWebPresence(string domain);
    Task DisablePublicWebPresence(string domain);

    Task<string> EnqueueDeleteTenant(string domain);
    Task<string> EnqueueExportTenant(string domain);
}
