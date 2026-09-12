using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.Admin.Tenants;
#nullable enable

/// <summary>
/// How an unregistered identity was discovered.
/// </summary>
public enum OrphanSource
{
    /// <summary>
    /// Found by a cross-tenant scan of the identity tables: the identity still owns rows.
    /// Postgres only. File and byte counts are real.
    /// </summary>
    Index,

    /// <summary>
    /// Found by scanning the registration root: a tenant directory whose id has no registration.
    /// File and byte counts are unknown (null); only the on-disk size is known.
    /// </summary>
    Directory,
}

/// <summary>
/// Per-identity storage and activity figures for the admin metrics endpoint.
/// Null means "not applicable or unknown", never "zero".
/// </summary>
public class TenantMetricsModel
{
    /// <summary>
    /// Canonical UUID string, always - never the raw or hex byte form. Present for every row,
    /// including identities that have no registration.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Null when the identity has no registration row: the registration is what carries the domain.
    /// </summary>
    public string? Domain { get; set; }

    public bool Registered { get; set; }

    /// <summary>
    /// Null when <see cref="Registered"/> is true.
    /// </summary>
    public OrphanSource? OrphanSource { get; set; }

    // --- registration facts; all null when !Registered ---

    public bool? Enabled { get; set; }
    public bool? EnablePublicWebPresence { get; set; }
    public string? Email { get; set; }
    public string? PlanId { get; set; }
    public UnixTimeUtc? CreatedAt { get; set; }
    public UnixTimeUtc? MarkedForDeletionDate { get; set; }

    /// <summary>
    /// Last time this identity was seen as the CALLER of a request to this node. For an owner or
    /// app session that is the tenant itself; for peer traffic the caller is a remote identity, so
    /// this table also holds subjects that are not tenants here. Null when never seen.
    /// </summary>
    public UnixTimeUtc? LastActivity { get; set; }

    // --- storage; null when unknown (a Directory orphan) ---

    public long? Files { get; set; }

    /// <summary>
    /// Every row, including soft-deleted tombstones, which keep a header-sized byteCount.
    /// Matches the existing tenant endpoint's payloadSize.
    /// </summary>
    public long? TotalBytes { get; set; }

    /// <summary>
    /// Rows in FileState.Active only. "How much would we restore" rather than "what are we billing".
    /// </summary>
    public long? ActiveBytes { get; set; }

    public int? DriveCount { get; set; }

    /// <summary>
    /// Bytes on local disk under the registration directory. On Postgres + S3 this is legitimately
    /// ~0: the tenant database is remote and payloads are in S3, so the directory holds little.
    /// </summary>
    public long? RegistrationSize { get; set; }

    /// <summary>
    /// Set when this tenant's figures could not be read. The storage fields are left null rather
    /// than zero, because a zero here is indistinguishable from the data loss this report exists
    /// to detect. Null on a healthy row.
    /// </summary>
    public string? MetricsError { get; set; }
}

public class TenantMetricsResponse
{
    public UnixTimeUtc GeneratedAt { get; set; }

    /// <summary>"sqlite" or "postgres".</summary>
    public string DatabaseType { get; set; } = "";

    /// <summary>
    /// False on SQLite, where each tenant has its own database file and there is nothing to group
    /// over. When false, orphans are only those found by the registration-directory scan, so an
    /// identity whose rows survived but whose directory was removed will not appear.
    /// </summary>
    public bool IndexOrphanScanSupported { get; set; }

    public List<TenantMetricsModel> Tenants { get; set; } = new();
}
