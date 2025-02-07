using System;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.SQLite.Migrations;

public static class BackupSqliteDatabase
{
    public static void Execute(string sourcePath, string destinationPath)
    {
        using DbConnection connection = new SqliteConnection($"Data Source={sourcePath}");
        connection.Open();

        using DbConnection backupConnection = new SqliteConnection($"Data Source={destinationPath}");
        ((SqliteConnection) connection).BackupDatabase((SqliteConnection) backupConnection);
    }
}
