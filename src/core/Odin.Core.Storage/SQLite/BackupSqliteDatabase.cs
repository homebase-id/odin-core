using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.SQLite;

public static class BackupSqliteDatabase
{
    public static void Execute(string sourcePath, string destinationPath)
    {
        SqliteJournalMode.SetDelete(sourcePath);

        using var cn = new SqliteConnection($"Data Source={sourcePath}");
        cn.Open();

        using var backupConnection = new SqliteConnection($"Data Source={destinationPath}");
        cn.BackupDatabase(backupConnection);
    }
}
