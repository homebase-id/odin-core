using System;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Services.Base;

namespace Odin.Services.Drives.Management;

#nullable enable

public enum DrivePurgeKind
{
    /// <summary>The drive stays; files created by <see cref="DrivePurge.CreatedAtOrBefore"/> go.</summary>
    Empty = 1,

    /// <summary>The drive record is already gone; every file and directory of it goes.</summary>
    Delete = 2
}

public class DrivePurge
{
    public Guid DriveId { get; init; }
    public DrivePurgeKind Kind { get; set; }

    /// <summary>For <see cref="DrivePurgeKind.Empty"/>: the moment the owner asked, in unix ms.</summary>
    public long CreatedAtOrBefore { get; set; }
}

/// <summary>
/// The drives whose content a <see cref="DrivePurgeJob"/> is still removing. While a drive is listed its alias
/// cannot be created again: the job deletes by drive id, and would take the new drive's files with it.
/// </summary>
public class DrivePurgeRegistry(TableKeyValueCached tblKeyValue)
{
    private static readonly SingleKeyValueStorage Storage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse("4f0a6d2e-6b3c-4f1e-9d2a-8c7b5e1f3a90"));

    public Task<DrivePurge?> GetAsync(Guid driveId) => Storage.GetAsync<DrivePurge>(tblKeyValue, driveId)!;

    public Task SaveAsync(DrivePurge purge) => Storage.UpsertAsync(tblKeyValue, purge.DriveId, purge);

    public Task RemoveAsync(Guid driveId) => Storage.DeleteAsync(tblKeyValue, driveId);
}
