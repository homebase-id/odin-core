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
// 14) Change to directory /identity-host/data/tenants/registrations
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

#if false

public static class MigrationTemplate
{
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
        var tenantId = Guid.Parse(Path.GetFileName(tenantDir));

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


        DoAThing(connectionString).GetAwaiter().GetResult();

    }

    private static async Task DoAThing(string connectionString)
    {
        await using var cn = await SqliteConcreteConnectionFactory.CreateAsync(connectionString);

        // Create table
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS driveLocalTagIndex("
                              +"identityId BYTEA NOT NULL, "
                              +"driveId BYTEA NOT NULL, "
                              +"fileId BYTEA NOT NULL, "
                              +"tagId BYTEA NOT NULL "
                              +", PRIMARY KEY (identityId,driveId,fileId,tagId)"
                              +");"
                              +"CREATE INDEX IF NOT EXISTS Idx0TableDriveLocalTagIndexCRUD ON driveLocalTagIndex(identityId,driveId,fileId);";
            await cmd.ExecuteNonQueryAsync();
        }

        // Alter table
        {
            await using var cmd2 = cn.CreateCommand();
            cmd2.CommandText = "ALTER TABLE driveMainIndex ADD COLUMN hdrLocalAppData TEXT;";
            await cmd2.ExecuteNonQueryAsync();
        }

        await using var tx = await cn.BeginTransactionAsync();

        // Insert data
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name, age) VALUES ('Alice', 30);";
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        // Query data
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT * FROM test;";
            var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync()) // Ensure you call ReadAsync before accessing data
            {
                var name = reader["name"].ToString();
            }
            else
            {
                // ...
            }
        }
    }
}

#endif