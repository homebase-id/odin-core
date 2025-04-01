using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.Factory.Sqlite;
using Odin.Core.Storage.SQLite.Migrations.Helpers;

namespace Odin.Core.Storage.SQLite.Migrations;

//
// MIGRATION steps
//
//  - Change to directory /identity-host
//  - Make sure container is stopped: docker compose down && docker container prune -f
//  - Build and deploy docker image with migration code - DO NOT START IT
//  - Change to directory /identity-host/data/
//  - Backup the system: sudo zip -r backup-system.zip system
//  - Change to directory /identity-host/data/tenants
//  - Backup the registrations: sudo zip -r backup-registrations.zip registrations
//  - Change to directory /identity-host
//  - Edit the docker-compose.yml file:
//    - Add the correct command line param to start the migration
//    - Disable start-always if enabled
//  - Start the docker image: docker compose up
//  - Wait for the migration to finish
//  - Make sure docker container is gone: docker container prune -f
//  - Redeploy the docker image (this will overwrite the compose changes from above) - START IT
//  - Run some smoke tests
//  - Check the logs for errors
//  - Change to directory /identity-host/data/
//  - Clean up: sudo rm backup-system.zip
//  - Change to directory /identity-host/data/tenants/registrations
//  - Clean up: sudo find . -type f -name 'oldidentity.*' -delete
//  - Clean up: sudo rm backup-registrations.zip
//
// ROLLBACK steps
//
//  - Change to directory /identity-host
//  - Make sure container is stopped: docker compose down && docker container prune -f
//  - Change to directory /identity-host/data/
//  - Remove system: sudo rm -rf system
//  - Restore system: sudo unzip backup-system.zip
//  - Change to directory /identity-host/data/tenants
//  - Remove registrations: sudo rm -rf registrations
//  - Restore registrations: sudo unzip backup-registrations.zip
//  - Redeploy the docker image (this will overwrite the compose changes) - START IT
//  - Change to directory /identity-host/data/
//  - Clean up: sudo rm backup-system.zip
//  - Change to directory /identity-host/data/tenants/registrations
//  - Clean up: sudo find . -type f -name 'oldidentity.*' -delete
//  - Clean up: sudo rm backup-registrations.zip
//

// Local test:
//
//   mkdir $HOME/tmp/example
//   rsync -rvz yagni.dk:/identity-host/data/system $HOME/tmp/example/data
//   rsync -rvz yagni.dk:/identity-host/data/tenants/registrations $HOME/tmp/example/data/tenants
// run params:
//   --my-migration-name $HOME/tmp/example/data

// PROD:
//
// run params:
//   --my-migration-name /identity-host/data

#if false

public static class MigrationTemplate
{
    public static void Execute(string dataRootPath)
    {
        var tenantDirs = Directory.GetDirectories(Path.Combine(dataRootPath, "tenants", "registrations"));
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