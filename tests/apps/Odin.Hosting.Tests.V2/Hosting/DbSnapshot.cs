#nullable enable
using System.IO;
using System.Threading.Tasks;
using Odin.Core.Storage.SQLite;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Per-tenant snapshot of an identity SQLite DB. <see cref="TakeAsync"/> copies the live file to a
/// sibling <c>.snap</c> via <see cref="BackupSqliteDatabase"/>; <see cref="RestoreAsync"/> copies it
/// back over the live file. Callers must ensure no open connections hold the live file when
/// restoring — in our framework <c>OdinHost.ResetAsync</c> clears the tenant's
/// <c>IDbConnectionPool</c> before calling <see cref="RestoreAsync"/>. The tenant lifetime scope
/// itself stays alive (see <see cref="OdinHost"/>.Snapshots.cs xmldoc).
/// </summary>
internal sealed class DbSnapshot
{
    public string Domain { get; }
    public string LiveDbPath { get; }
    public string SnapshotPath { get; }

    public DbSnapshot(string domain, string liveDbPath)
    {
        Domain = domain;
        LiveDbPath = liveDbPath;
        SnapshotPath = liveDbPath + ".snap";
    }

    public Task TakeAsync()
    {
        BackupSqliteDatabase.Execute(LiveDbPath, SnapshotPath);
        return Task.CompletedTask;
    }

    public Task RestoreAsync()
    {
        File.Copy(SnapshotPath, LiveDbPath, overwrite: true);
        return Task.CompletedTask;
    }
}
