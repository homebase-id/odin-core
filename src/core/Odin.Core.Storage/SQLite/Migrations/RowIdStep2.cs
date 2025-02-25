using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.SQLite.Migrations;

//
// MIGRATION steps
//
//  0) Change to directory /identity-host
//  1) Make sure container is stopped: docker compose down
//  2) Make sure container is gone: docker container prune
//  3) Build and deploy docker image with migration code - DO NOT START IT
//  4) Change to directory /identity-host/data/tenants
//  5) Backup the registrations: sudo zip -r registrations-backup.zip registrations
//  6) Change to directory /identity-host
//  7) Edit the docker-compose.yml file, add the correct command line param to start the migration
//  8) Start the docker image: docker compose up
//  9) Wait for the migration to finish
// 10) Make sure docker container is gone: docker container prune
// 11) Redeploy the docker image (this will overwrite the compose changes from above) - START IT
// 12) Run some smoke tests
// 13) Check the logs for errors
// 14) Change to directory /identity-host/data/tenants
// 15) clean up: sudo find . -type f -name 'oldidentity.*' -delete
// 16) clean up: sudo rm registrations-backup.zip
//
// PANIC steps
//
//  0) Change to directory /identity-host
//  1) Make sure container is stopped: docker compose down
//  2) Make sure container is gone: docker container prune
//  3) Change to directory /identity-host/data/tenants
//  4) Remove registrations: sudo rm -rf registrations
//  5) Restore registrations: sudo unzip registrations-backup.zip
//  6) Redeploy the docker image (this will overwrite the compose changes) - START IT
//

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

