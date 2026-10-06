using Odin.Core.Time;
using Odin.Services.Registry;

namespace Odin.Services.Admin.Tenants;
#nullable enable

public class TenantModel
{
    public string Domain { get; set; } = "";
    public string Id { get; set; } = "";
    public string RegistrationPath { get; set; } = "";
    public long RegistrationSize { get; set; } = 0;

    public TenantStatus Status { get; set; }

    /// <summary>
    /// Kept for clients that predate <see cref="Status"/>: false only when disabled
    /// </summary>
    public bool Enabled => Status != TenantStatus.Disabled;
    public DisabledReason? DisabledReason { get; set; }
    public UnixTimeUtc? StatusChangedAt { get; set; }
    public bool EnablePublicWebPresence { get; set; }
    public string? PayloadPath { get; set; } = null;
    public long? PayloadSize { get; set; } = null;

    /// <summary>As <see cref="TenantMetricsModel.CreatedAt"/>. A move keeps it.</summary>
    public UnixTimeUtc? Created { get; set; }

    /// <summary>
    /// As <see cref="TenantMetricsModel.LastActivity"/>: kept for 365 days, per host, and not carried by a move.
    /// </summary>
    public UnixTimeUtc? LastActivity { get; set; }

    /// <summary>No activity on this host in the last <paramref name="days"/> days, or none at all.</summary>
    public bool InactiveFor(int days, UnixTimeUtc now) => LastActivity == null || LastActivity.Value < now.AddDays(-days);
}