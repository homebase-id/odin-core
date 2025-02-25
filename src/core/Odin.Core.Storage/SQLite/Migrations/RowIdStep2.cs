using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.SQLite.Migrations;

// Local test:
//   mkdir $HOME/tmp/rowidstep2
//   rsync -rvz yagni.dk:/identity-host/data/system $HOME/rowidstep2/data
//   rsync -rvz yagni.dk:/identity-host/data/tenants/registrations $HOME/rowidstep2/data/tenants
// run params:
//   --rowidstep2 $HOME/tmp/rowidstep2/data

// PROD:
// run params:
//   --rowidstep2 /identity-host/data


public static class RowIdStep2
{
    public static void Execute(string dataRootPath)
    {
        var systemDir = Path.Combine(dataRootPath, "system");
        DoSystemDir(systemDir);

        var tenantDirs = Directory.GetDirectories(Path.Combine(dataRootPath, "tenants", "registrations"));
        foreach (var tenantDir in tenantDirs)
        {
            DoTenantDir(tenantDir);
        }
    }

    //

    private static void DoSystemDir(string systemDir)
    {
        Console.WriteLine(systemDir);

        var orgDbPath = Path.Combine(systemDir, "sys.db");
        var oldDbPath = Path.Combine(systemDir, "oldsys.db");

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

        DoSystem(connectionString).GetAwaiter().GetResult();
    }

    //

    private static async Task DoSystem(string connectionString)
    {
        await using var cn = await SqliteConcreteConnectionFactory.CreateAsync(connectionString);
        await using var tx = await cn.BeginTransactionAsync();

        const string sql =
            """
            UPDATE Jobs 
            SET created = CASE WHEN created > (1 << 42) THEN created >> 16 ELSE created END, 
                modified = CASE WHEN modified IS NOT NULL AND modified > (1 << 42) THEN modified >> 16 ELSE modified END;
            """;

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();

        await tx.CommitAsync();
    }

    //

    private static void DoTenantDir(string tenantDir)
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

        DoTenant(connectionString).GetAwaiter().GetResult();
    }

    //

    private static async Task DoTenant(string connectionString)
    {
        await using var cn = await SqliteConcreteConnectionFactory.CreateAsync(connectionString);
        await using var tx = await cn.BeginTransactionAsync();

        const string sql =
            """
            UPDATE AppNotifications 
            SET created = CASE WHEN created > (1 << 42) THEN created >> 16 ELSE created END, 
                modified = CASE WHEN modified IS NOT NULL AND modified > (1 << 42) THEN modified >> 16 ELSE modified END;

            UPDATE Connections 
            SET created = CASE WHEN created > (1 << 42) THEN created >> 16 ELSE created END, 
                modified = CASE WHEN modified IS NOT NULL AND modified > (1 << 42) THEN modified >> 16 ELSE modified END;

            UPDATE imFollowing 
            SET created = CASE WHEN created > (1 << 42) THEN created >> 16 ELSE created END, 
                modified = CASE WHEN modified IS NOT NULL AND modified > (1 << 42) THEN modified >> 16 ELSE modified END;

            UPDATE FollowsMe 
            SET created = CASE WHEN created > (1 << 42) THEN created >> 16 ELSE created END, 
                modified = CASE WHEN modified IS NOT NULL AND modified > (1 << 42) THEN modified >> 16 ELSE modified END;

            UPDATE Inbox 
            SET created = CASE WHEN created > (1 << 42) THEN created >> 16 ELSE created END, 
                modified = CASE WHEN modified IS NOT NULL AND modified > (1 << 42) THEN modified >> 16 ELSE modified END;

            UPDATE Outbox 
            SET created = CASE WHEN created > (1 << 42) THEN created >> 16 ELSE created END, 
                modified = CASE WHEN modified IS NOT NULL AND modified > (1 << 42) THEN modified >> 16 ELSE modified END;

            UPDATE DriveMainIndex 
            SET created = CASE WHEN created > (1 << 42) THEN created >> 16 ELSE created END, 
                modified = CASE WHEN modified IS NOT NULL AND modified > (1 << 42) THEN modified >> 16 ELSE modified END;
            """;

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();

        await tx.CommitAsync();
    }
}

