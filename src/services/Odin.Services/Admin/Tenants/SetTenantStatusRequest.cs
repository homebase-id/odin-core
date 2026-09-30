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

    /// <summary>
    /// Takes a copy disabled as moved back to <see cref="TenantStatus.Paused"/>, and nothing else: rolling a move back is
    /// an operator's explicit decision (<see cref="TenantStatusRules.Validate"/>)
    /// </summary>
    public bool UnlockMoved { get; set; }
}
