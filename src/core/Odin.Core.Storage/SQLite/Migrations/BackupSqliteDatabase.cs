using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.SQLite.Migrations;

public static class BackupSqliteDatabase
{
    public static void Execute(string sourcePath, string destinationPath)
    {
        SqliteJournalMode.SetDelete(sourcePath);

        using DbConnection cn = new SqliteConnection($"Data Source={sourcePath}");
        cn.Open();

        using DbConnection backupConnection = new SqliteConnection($"Data Source={destinationPath}");
        ((SqliteConnection) cn).BackupDatabase((SqliteConnection) backupConnection);
    }
}
