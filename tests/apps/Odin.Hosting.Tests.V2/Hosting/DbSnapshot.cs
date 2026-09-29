#nullable enable
using System.Threading.Tasks;
using Odin.Core.Storage.SQLite;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Per-tenant snapshot of an identity SQLite DB. Both directions go through SQLite's backup API
/// (<see cref="BackupSqliteDatabase"/>): <see cref="TakeAsync"/> copies the live database to a
/// sibling <c>.snap</c>, and <see cref="RestoreAsync"/> writes it back into the live one.
/// </summary>
/// <remarks>
/// Restore used to be <c>File.Copy</c> over the live file. That is only safe with every connection
/// closed, and a request still in flight from the previous test keeps one open. In WAL mode the
/// surviving -wal file is then replayed over the copied-in file, so the "reset" silently keeps the
/// previous test's writes. Restoring through SQLite is correct with connections open, and leaves
/// the database in WAL mode, as production runs it.
/// </remarks>
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
        BackupSqliteDatabase.Execute(SnapshotPath, LiveDbPath);
        return Task.CompletedTask;
    }
}
