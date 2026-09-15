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

    /// <summary>
    /// Sets a disabled tenant to <see cref="TenantStatus.Active"/>. Only undoes a disable: a paused or
    /// out-of-quota tenant is left alone.
    /// </summary>
    Task EnableTenant(string domain);

    /// <summary>
    /// Sets the tenant to <see cref="TenantStatus.Disabled"/> with <see cref="DisabledReason.Admin"/>.
    /// An already disabled tenant keeps its reason.
    /// </summary>
    Task DisableTenant(string domain);

    /// <summary>
    /// Sets the tenant's status. Returns the previous status, or null if the tenant does not exist.
    /// </summary>
    Task<TenantStatusState?> SetTenantStatusAsync(string domain, TenantStatus status, DisabledReason? reason);

    Task EnablePublicWebPresence(string domain);
    Task DisablePublicWebPresence(string domain);

    Task<string> EnqueueDeleteTenant(string domain);
    Task<string> EnqueueExportTenant(string domain);
}
