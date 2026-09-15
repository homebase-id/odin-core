using System.ComponentModel.DataAnnotations;
using Odin.Core.Time;
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

public class TenantStatusModel
{
    public TenantStatus Status { get; set; }
    public DisabledReason? DisabledReason { get; set; }
    public UnixTimeUtc? StatusChangedAt { get; set; }

    public static TenantStatusModel From(TenantStatusState state)
    {
        return new TenantStatusModel
        {
            Status = state.Status,
            DisabledReason = state.DisabledReason,
            StatusChangedAt = state.StatusChangedAt
        };
    }
}
