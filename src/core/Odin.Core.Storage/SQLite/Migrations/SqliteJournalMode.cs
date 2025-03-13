using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.SQLite.Migrations;

public static class SqliteJournalMode
{
    // Temporaroly get rid of those wal and shm files
    public static void SetDelete(string dbPath)
    {
        using DbConnection cn = new SqliteConnection($"Data Source={dbPath}");
        cn.Open();

        using var cmd = cn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = DELETE;";
        cmd.ExecuteNonQuery();
    }

}