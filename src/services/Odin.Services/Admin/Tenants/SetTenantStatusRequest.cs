using System.ComponentModel.DataAnnotations;
using Odin.Services.Registry;

namespace Odin.Services.Admin.Tenants;
#nullable enable

public class SetTenantStatusRequest
{
    /// <summary>
    /// Required: a missing status must not silently mean <see cref="TenantStatus.Active"/>
    /// </summary>
    [Required]
    public TenantStatus? Status { get; set; }

    /// <summary>
    /// Only valid with <see cref="TenantStatus.Disabled"/>; defaults to <see cref="Registry.DisabledReason.Admin"/>
    /// </summary>
    public DisabledReason? DisabledReason { get; set; }
}
