using System;
using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.SQLite.Migrations;

public static class BackupSqliteDatabase
{
    public static string Execute(string sourcePath)
    {
        // var backupPath = $"{sourcePath}.{DateTimeOffset.Now.ToUnixTimeMilliseconds()}";
        var backupPath = $"{sourcePath}.backup";

        using var connection = new SqliteConnection($"Data Source={sourcePath}");
        connection.Open();

        using var backupConnection = new SqliteConnection($"Data Source={backupPath}");
        connection.BackupDatabase(backupConnection);

        return backupPath;
    }
}
