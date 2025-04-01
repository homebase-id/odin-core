using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.SQLite.Migrations.Helpers;

public static class SqliteJournalMode
{
    // Temporarily get rid of those wal and shm files
    public static void SetDelete(string dbPath)
    {
        using var cn = new SqliteConnection($"Data Source={dbPath}");
        cn.Open();

        using var cmd = cn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = DELETE;";
        cmd.ExecuteNonQuery();
    }
}