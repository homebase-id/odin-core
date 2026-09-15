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
}