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

    /// <summary>When the identity was registered. A move keeps it.</summary>
    public UnixTimeUtc? Created { get; set; }

    /// <summary>
    /// The last request made as this identity on this host: its owner or apps, or the identity calling a peer hosted
    /// here, its own background jobs included. Not an owner login. Null when not seen here in the last 365 days; a
    /// move does not carry it.
    /// </summary>
    public UnixTimeUtc? LastActivity { get; set; }

    /// <summary>No activity on this host in the last <paramref name="days"/> days, or none at all.</summary>
    public bool InactiveFor(int days, UnixTimeUtc now) => LastActivity == null || LastActivity.Value < now.AddDays(-days);
}