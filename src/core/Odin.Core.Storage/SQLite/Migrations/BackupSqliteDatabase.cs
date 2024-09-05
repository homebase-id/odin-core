using System;
using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.SQLite.Migrations;

public static class BackupSqliteDatabase
{
    public static void Execute(string sourcePath, string destinationPath)
    {
        using var connection = new SqliteConnection($"Data Source={sourcePath}");
        connection.Open();

        using var backupConnection = new SqliteConnection($"Data Source={destinationPath}");
        connection.BackupDatabase(backupConnection);
    }
}
