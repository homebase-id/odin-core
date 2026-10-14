using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Odin.Core;
using Odin.Core.Time;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Services.Base;

namespace Odin.Services.Drives.Management;

#nullable enable

public enum DrivePurgeKind
{
    /// <summary>The drive stays; files created by <see cref="DrivePurge.Requested"/> go.</summary>
    Empty = 1,

    /// <summary>The drive record is already gone; every file and directory of it goes.</summary>
    Delete = 2
}

public class DrivePurge
{
    public Guid DriveId { get; init; }

    /// <summary>Kept here because a deleted drive no longer has a record to describe it.</summary>
    public TargetDrive TargetDrive { get; init; } = null!;

    public string? Name { get; init; }

    /// <summary>The app that owned the drive, so the console can show its purges on that app's page.</summary>
    public Guid? AppId { get; init; }

    public DrivePurgeKind Kind { get; set; }

    /// <summary>When the owner asked, in unix ms. Emptying spares files created after it.</summary>
    public long Requested { get; set; }

    /// <summary>The last run's failure, cleared by a run that makes progress.</summary>
    public string? LastError { get; set; }

    public long? LastErrorAt { get; set; }

    /// <summary>Files created by this time are purged; none for a delete, which takes every file.</summary>
    [JsonIgnore]
    public long? Cutoff => Kind == DrivePurgeKind.Empty ? Requested : null;

    public static DrivePurge For(StorageDrive drive, DrivePurgeKind kind) => new()
    {
        DriveId = drive.Id,
        TargetDrive = drive.TargetDriveInfo,
        Name = drive.Name,
        AppId = drive.AppId,
        Kind = kind,
        Requested = UnixTimeUtc.Now().milliseconds
    };

    /// <summary>Records a run's failure, or with null clears it.</summary>
    public void SetError(string? error)
    {
        LastError = error;
        LastErrorAt = error == null ? null : UnixTimeUtc.Now().milliseconds;
    }
}

/// <summary>
/// The drives whose content a <see cref="DrivePurgeJob"/> is still removing. While a drive is listed its alias
/// cannot be created again: the job deletes by drive id, and would take the new drive's files with it.
/// </summary>
public class DrivePurgeRegistry(TableKeyThreeValueCached tblKeyThreeValue)
{
    private static readonly ThreeKeyValueStorage Storage =
        TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse("4f0a6d2e-6b3c-4f1e-9d2a-8c7b5e1f3a90"));

    private static readonly byte[] DataType = Guid.Parse("8b1d0e5c-2f47-4a39-b6c8-1e9f7a3d5c20").ToByteArray();

    public Task<DrivePurge?> GetAsync(Guid driveId) => Storage.GetAsync<DrivePurge>(tblKeyThreeValue, driveId)!;

    public async Task<List<DrivePurge>> GetAllAsync() =>
        (await Storage.GetByDataTypeAsync<DrivePurge>(tblKeyThreeValue, DataType)).ToList();

    public Task SaveAsync(DrivePurge purge) =>
        Storage.UpsertAsync(tblKeyThreeValue, purge.DriveId, DataType, DataType, purge);

    public Task RemoveAsync(Guid driveId) => Storage.DeleteAsync(tblKeyThreeValue, driveId);
}
