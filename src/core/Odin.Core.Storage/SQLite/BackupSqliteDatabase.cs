using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.SQLite;

public static class BackupSqliteDatabase
{
    private static readonly TimeSpan BusyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BusyRetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Copies <paramref name="sourcePath"/> into <paramref name="destinationPath"/> through SQLite's
    /// online backup API, which takes a consistent image of a live database in any journal mode and
    /// writes the destination through SQLite, so connections already open on either file see a
    /// consistent result. Both directions work: snapshot a live database, or restore one in place.
    /// </summary>
    /// <remarks>
    /// This used to switch the source to <c>journal_mode=DELETE</c> first, to be rid of the -wal and
    /// -shm files, and never switched it back. The product sets WAL once per database per process,
    /// so the live database then ran in rollback-journal mode for the rest of the process - where
    /// readers and writers block each other and can deadlock, which WAL never does. Every V2 test
    /// after its fixture's baseline ran that way.
    ///
    /// Retries while the destination is locked: unlike ordinary commands, Microsoft.Data.Sqlite's
    /// backup call fails at once on a busy database instead of waiting.
    /// </remarks>
    public static void Execute(string sourcePath, string destinationPath)
    {
        using var source = new SqliteConnection(ConnectionString(sourcePath));
        source.Open();

        using var destination = new SqliteConnection(ConnectionString(destinationPath));
        destination.Open();

        var sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                source.BackupDatabase(destination);
                return;
            }
            catch (SqliteException e) when (e.SqliteErrorCode is 5 or 6 && sw.Elapsed < BusyTimeout) // SQLITE_BUSY, SQLITE_LOCKED
            {
                Thread.Sleep(BusyRetryDelay);
            }
        }
    }

    // Not pooled: a pooled handle would outlive this call and keep the files open.
    private static string ConnectionString(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
}
