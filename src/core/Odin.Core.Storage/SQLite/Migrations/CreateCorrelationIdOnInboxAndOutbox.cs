using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.SQLite.Migrations;

public static class CreateCorrelationIdOnInboxAndOutbox
{
    // tenantDataRootPath in PROD: /identity-host/data/tenants
    public static void Execute(string tenantDataRootPath)
    {
        var tenantDirs = Directory.GetDirectories(Path.Combine(tenantDataRootPath, "registrations"));
        foreach (var tenantDir in tenantDirs)
        {
            DoDatabase(tenantDir);
        }
    }

    //

    private static void DoDatabase(string tenantDir)
    {
        Console.WriteLine(tenantDir);

        var orgDbPath = Path.Combine(tenantDir, "headers", "identity.db");
        var oldDbPath = Path.Combine(tenantDir, "headers", "oldidentity.db");

        if (!File.Exists(orgDbPath))
        {
            throw new Exception("Database not found: " + orgDbPath);
        }

        if (File.Exists(oldDbPath)) File.Delete(oldDbPath);
        BackupSqliteDatabase.Execute(orgDbPath, oldDbPath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = orgDbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        CreateCorrelationId(connectionString).GetAwaiter().GetResult();
    }

    private static async Task CreateCorrelationId(string connectionString)
    {
        await using var cn = await SqliteConcreteConnectionFactory.CreateAsync(connectionString);

        // Alter table inbox
        {
            await using var cmd2 = cn.CreateCommand();
            cmd2.CommandText = "ALTER TABLE inbox ADD COLUMN correlationId TEXT;";
            await cmd2.ExecuteNonQueryAsync();
            Console.WriteLine("Added correlationId column to inbox");
        }

        // Alter table outbox
        {
            await using var cmd2 = cn.CreateCommand();
            cmd2.CommandText = "ALTER TABLE outbox ADD COLUMN correlationId TEXT;";
            await cmd2.ExecuteNonQueryAsync();
            Console.WriteLine("Added correlationId column to outbox");
        }
    }
}