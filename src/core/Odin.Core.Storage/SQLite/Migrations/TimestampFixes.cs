using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
// run params:
//   --timefixes /home/seb/tmp/timefix/olddata /home/seb/tmp/timefix/data

// PROD:
//
// run params:
//   --timefixes /tmp/xx-olddata /identity-host/data

public static class TimestampFixes
{
    public static void Execute(string oldDataRootPath, string dataRootPath)
    {
        var tenantDirs = Directory.GetDirectories(Path.Combine(dataRootPath, "tenants", "registrations"));
        foreach (var tenantDir in tenantDirs)
        {
            var oldTenantDir = Path.Combine(oldDataRootPath, "tenants", "registrations", Path.GetFileName(tenantDir));
            Console.WriteLine($"Tenant {tenantDir} - fixing timestamps from {oldTenantDir}");
            DoDatabase(oldTenantDir, tenantDir);
        }
    }

    //

    private static void DoDatabase(string oldTenantDir, string tenantDir)
    {
        var tenantId = Guid.Parse(Path.GetFileName(tenantDir));

        var currentDbPath = Path.Combine(tenantDir, "headers", "identity.db");
        var originalDbPath = Path.Combine(oldTenantDir, "headers", "identity.db");

        if (!File.Exists(currentDbPath))
        {
            throw new Exception("Database not found: " + currentDbPath);
        }

        if (!File.Exists(originalDbPath))
        {
            throw new Exception("Database not found: " + originalDbPath);
        }

        {
            // Back-up the current database before we make any changes
            var backupDbPath = Path.Combine(tenantDir, "headers", "identity.db.20250408.backup");
            // DO NOT DELETE THE BACKUP DATABASE. WE WANT TO BACK IT UP EXACTLY ONCE, 
            // AND IF IT ALREADY EXISTS, THEN LEAVE IT SO WE DONT RISK OVERWRITING IT
            // IF BY ACCIDENT WE RUN IT TWICE
            if (!File.Exists(backupDbPath))
            {
                BackupSqliteDatabase.Execute(currentDbPath, backupDbPath);
            }
        }

        var currentConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = currentDbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        var originalConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = originalDbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        DoAThing(originalConnectionString, currentConnectionString).GetAwaiter().GetResult();

        // Clean it up now that we're touching it anyways
        Vaccuum(currentConnectionString).GetAwaiter().GetResult();
    }

    private static async Task Vaccuum(string currentConnectionString)
    {
        await using var cnOrg = await SqliteConcreteConnectionFactory.CreateAsync(currentConnectionString);
        await cnOrg.OpenAsync();
        using var cmd = cnOrg.CreateCommand();
        cmd.CommandText = "VACUUM";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task FixTimes(DbConnection cnOrg, string tableName)
    {
        // Make sure we didn't get any old format timestamps
        using (var updateCmd = cnOrg.CreateCommand())
        {
            updateCmd.CommandText = @$"
                        UPDATE {tableName}
                        SET created = created >> 12
                        WHERE created > (1 << 42)";
            await updateCmd.ExecuteNonQueryAsync();
        }

        // Make sure we didn't get any old format timestamps
        using (var updateCmd = cnOrg.CreateCommand())
        {
            updateCmd.CommandText = @$"
                        UPDATE {tableName}
                        SET modified = modified >> 12
                        WHERE modified IS NOT NULL AND modified > (1 << 42)";
            await updateCmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task DoAThing(string originalConnectionString, string currentConnectionString)
    {
        // Use only the main database connection
        await using var cnOrg = await SqliteConcreteConnectionFactory.CreateAsync(currentConnectionString);

        try
        {
            // Attach the backup database to the main connection
            using (var attachCmd = cnOrg.CreateCommand())
            {
                attachCmd.CommandText = "ATTACH DATABASE @originalPath AS original";
                var param = attachCmd.CreateParameter();
                param.ParameterName = "@originalPath";
                string result = originalConnectionString.Split('=')[1].Split(';')[0];
                param.Value = result;
                attachCmd.Parameters.Add(param);
                await attachCmd.ExecuteNonQueryAsync();
            }

            // AppNotifications
            using (var selectCmd = cnOrg.CreateCommand())
            {
                selectCmd.CommandText = @$"
                    SELECT created,modified,identityId,notificationId
                    FROM original.AppNotifications
                    ORDER BY created ASC;";

                using (var rdr = await selectCmd.ExecuteReaderAsync(CommandBehavior.Default))
                {
                    long _created;
                    long? _modified;
                    Guid _identityId;
                    Guid _notificationId;

                    int countCreated = 0;
                    int countModified = 0;
                    while (await rdr.ReadAsync())
                    {
                        _created = (long)rdr[0];
                        _modified = (rdr[1] == DBNull.Value) ? null : (long)rdr[1];
                        _identityId = new Guid((byte[])rdr[2]);
                        _notificationId = new Guid((byte[])rdr[3]);

                        using (var updateCmd = cnOrg.CreateCommand())
                        {
                            updateCmd.CommandText = @$"
                                UPDATE AppNotifications
                                SET created = @created
                                WHERE identityId=@identityId AND notificationId=@notificationId";

                            var param1 = updateCmd.CreateParameter();
                            var param2 = updateCmd.CreateParameter();
                            var param4 = updateCmd.CreateParameter();

                            param1.ParameterName = "@identityId";
                            param2.ParameterName = "@notificationId";
                            param4.ParameterName = "@created";

                            param1.Value = _identityId.ToByteArray();
                            param2.Value = _notificationId.ToByteArray();
                            param4.Value = _created;

                            updateCmd.Parameters.Add(param1);
                            updateCmd.Parameters.Add(param2);
                            updateCmd.Parameters.Add(param4);

                            int n = await updateCmd.ExecuteNonQueryAsync();

                            countCreated += n;
                        }

                        using (var updateCmd = cnOrg.CreateCommand())
                        {
                            if (_modified == null)
                                continue;

                            updateCmd.CommandText = @$"
                                UPDATE AppNotifications
                                SET modified = @modified
                                WHERE identityId=@identityId AND notificationId=@notificationId AND modified IS NULL";

                            var param1 = updateCmd.CreateParameter();
                            var param2 = updateCmd.CreateParameter();
                            var param4 = updateCmd.CreateParameter();

                            param1.ParameterName = "@identityId";
                            param2.ParameterName = "@notificationId";
                            param4.ParameterName = "@modified";

                            param1.Value = _identityId.ToByteArray();
                            param2.Value = _notificationId.ToByteArray();
                            param4.Value = _modified;

                            updateCmd.Parameters.Add(param1);
                            updateCmd.Parameters.Add(param2);
                            updateCmd.Parameters.Add(param4);

                            int n = await updateCmd.ExecuteNonQueryAsync();

                            countModified += n;
                        }
                    } // while
                    Console.WriteLine($"AppNotifications Created records fixed = {countCreated}");
                    Console.WriteLine($"AppNotifications Modified records fixed = {countModified}");
                }

                await FixTimes(cnOrg, "AppNotifications");
            } // AppNotifications

            // Connections
            using (var selectCmd = cnOrg.CreateCommand())
            {
                selectCmd.CommandText = @$"
                    SELECT created,modified,identityId,identity
                    FROM original.Connections
                    ORDER BY created ASC;";

                using (var rdr = await selectCmd.ExecuteReaderAsync(CommandBehavior.Default))
                {
                    long _created;
                    long? _modified;
                    Guid _identityId;
                    string _identity;

                    int countCreated = 0;
                    int countModified = 0;
                    while (await rdr.ReadAsync())
                    {
                        _created = (long)rdr[0];
                        _modified = (rdr[1] == DBNull.Value) ? null : (long)rdr[1];
                        _identityId = new Guid((byte[])rdr[2]);
                        _identity = ((string)rdr[3]);

                        using (var updateCmd = cnOrg.CreateCommand())
                        {
                            updateCmd.CommandText = @$"
                                UPDATE Connections
                                SET created = @created
                                WHERE identityId=@identityId AND identity=@identity";

                            var param1 = updateCmd.CreateParameter();
                            var param2 = updateCmd.CreateParameter();
                            var param4 = updateCmd.CreateParameter();

                            param1.ParameterName = "@identityId";
                            param2.ParameterName = "@identity";
                            param4.ParameterName = "@created";

                            param1.Value = _identityId.ToByteArray();
                            param2.Value = _identity;
                            param4.Value = _created;

                            updateCmd.Parameters.Add(param1);
                            updateCmd.Parameters.Add(param2);
                            updateCmd.Parameters.Add(param4);

                            int n = await updateCmd.ExecuteNonQueryAsync();

                            countCreated += n;
                        }

                        using (var updateCmd = cnOrg.CreateCommand())
                        {
                            if (_modified == null)
                                continue;

                            updateCmd.CommandText = @$"
                                UPDATE Connections
                                SET modified = @modified
                                WHERE identityId=@identityId AND identity=@identity AND modified IS NULL";

                            var param1 = updateCmd.CreateParameter();
                            var param2 = updateCmd.CreateParameter();
                            var param4 = updateCmd.CreateParameter();

                            param1.ParameterName = "@identityId";
                            param2.ParameterName = "@identity";
                            param4.ParameterName = "@modified";

                            param1.Value = _identityId.ToByteArray();
                            param2.Value = _identity;
                            param4.Value = _modified;

                            updateCmd.Parameters.Add(param1);
                            updateCmd.Parameters.Add(param2);
                            updateCmd.Parameters.Add(param4);

                            int n = await updateCmd.ExecuteNonQueryAsync();

                            countModified += n;
                        }
                    }
                    Console.WriteLine($"Connections Created records fixed = {countCreated}");
                    Console.WriteLine($"Connections Modified records fixed = {countModified}");
                }

                await FixTimes(cnOrg, "AppNotifications");
            } // Connections


            // driveMainIndex
            using (var selectCmd = cnOrg.CreateCommand())
            {
                selectCmd.CommandText = @$"
                    SELECT created,modified,identityId,driveId,fileId
                    FROM original.driveMainIndex
                    ORDER BY created ASC;";

                using (var rdr = await selectCmd.ExecuteReaderAsync(CommandBehavior.Default))
                {
                    long _created;
                    long? _modified;
                    Guid _identityId;
                    Guid _driveId;
                    Guid _fileId;

                    int countCreated = 0;
                    int countModified = 0;
                    while (await rdr.ReadAsync())
                    {
                        _created = (long)rdr[0];
                        _modified = (rdr[1] == DBNull.Value) ? null : (long)rdr[1];
                        _identityId = new Guid((byte[])rdr[2]);
                        _driveId = new Guid((byte[])rdr[3]);
                        _fileId = new Guid((byte[])rdr[4]);

                        using (var updateCmd = cnOrg.CreateCommand())
                        {
                            updateCmd.CommandText = @$"
                                UPDATE driveMainIndex
                                SET created = @created
                                WHERE identityId=@identityId AND driveId=@driveId AND fileId = @fileId";

                            var param1 = updateCmd.CreateParameter();
                            var param2 = updateCmd.CreateParameter();
                            var param3 = updateCmd.CreateParameter();
                            var param4 = updateCmd.CreateParameter();

                            param1.ParameterName = "@identityId";
                            param2.ParameterName = "@driveId";
                            param3.ParameterName = "@fileId";
                            param4.ParameterName = "@created";

                            param1.Value = _identityId.ToByteArray();
                            param2.Value = _driveId.ToByteArray();
                            param3.Value = _fileId.ToByteArray();
                            param4.Value = _created;

                            updateCmd.Parameters.Add(param1);
                            updateCmd.Parameters.Add(param2);
                            updateCmd.Parameters.Add(param3);
                            updateCmd.Parameters.Add(param4);

                            int n = await updateCmd.ExecuteNonQueryAsync();

                            countCreated += n;
                        }

                        using (var updateCmd = cnOrg.CreateCommand())
                        {
                            if (_modified == null)
                                continue;

                            updateCmd.CommandText = @$"
                                UPDATE driveMainIndex
                                SET modified = @modified
                                WHERE identityId=@identityId AND driveId=@driveId AND fileId = @fileId AND modified IS NULL";

                            var param1 = updateCmd.CreateParameter();
                            var param2 = updateCmd.CreateParameter();
                            var param3 = updateCmd.CreateParameter();
                            var param4 = updateCmd.CreateParameter();

                            param1.ParameterName = "@identityId";
                            param2.ParameterName = "@driveId";
                            param3.ParameterName = "@fileId";
                            param4.ParameterName = "@modified";

                            param1.Value = _identityId.ToByteArray();
                            param2.Value = _driveId.ToByteArray();
                            param3.Value = _fileId.ToByteArray();
                            param4.Value = _modified;

                            updateCmd.Parameters.Add(param1);
                            updateCmd.Parameters.Add(param2);
                            updateCmd.Parameters.Add(param3);
                            updateCmd.Parameters.Add(param4);

                            int n = await updateCmd.ExecuteNonQueryAsync();

                            countModified += n;
                        }
                    }
                    Console.WriteLine($"DriveMainIndex Created records fixed = {countCreated}");
                    Console.WriteLine($"DriveMainIndex Modified records fixed = {countModified}");
                }

                await FixTimes(cnOrg, "DriveMainIndex");
            } // driveMainIndex


            // followsMe
            using (var selectCmd = cnOrg.CreateCommand())
            {
                selectCmd.CommandText = @$"
                    SELECT created,modified,identityId,identity,driveId
                    FROM original.followsMe
                    ORDER BY created ASC;";

                using (var rdr = await selectCmd.ExecuteReaderAsync(CommandBehavior.Default))
                {
                    long _created;
                    long? _modified;
                    Guid _identityId;
                    string _identity;
                    Guid _driveId;

                    int countCreated = 0;
                    int countModified = 0;
                    while (await rdr.ReadAsync())
                    {
                        _created = (long)rdr[0];
                        _modified = (rdr[1] == DBNull.Value) ? null : (long)rdr[1];
                        _identityId = new Guid((byte[])rdr[2]);
                        _identity = (string)rdr[3];
                        _driveId = new Guid((byte[])rdr[4]);

                        using (var updateCmd = cnOrg.CreateCommand())
                        {
                            updateCmd.CommandText = @$"
                                UPDATE followsMe
                                SET created = @created
                                WHERE identityId=@identityId AND identity=@identity AND driveId = @driveId";

                            var param1 = updateCmd.CreateParameter();
                            var param2 = updateCmd.CreateParameter();
                            var param3 = updateCmd.CreateParameter();
                            var param4 = updateCmd.CreateParameter();

                            param1.ParameterName = "@identityId";
                            param2.ParameterName = "@identity";
                            param3.ParameterName = "@driveId";
                            param4.ParameterName = "@created";

                            param1.Value = _identityId.ToByteArray();
                            param2.Value = _identity;
                            param3.Value = _driveId.ToByteArray();
                            param4.Value = _created;

                            updateCmd.Parameters.Add(param1);
                            updateCmd.Parameters.Add(param2);
                            updateCmd.Parameters.Add(param3);
                            updateCmd.Parameters.Add(param4);

                            int n = await updateCmd.ExecuteNonQueryAsync();

                            countCreated += n;
                        }

                        using (var updateCmd = cnOrg.CreateCommand())
                        {
                            if (_modified == null)
                                continue;

                            updateCmd.CommandText = @$"
                                UPDATE followsMe
                                SET modified = @modified
                                WHERE identityId=@identityId AND identity=@identity AND driveId = @driveId AND modified IS NULL";

                            var param1 = updateCmd.CreateParameter();
                            var param2 = updateCmd.CreateParameter();
                            var param3 = updateCmd.CreateParameter();
                            var param4 = updateCmd.CreateParameter();

                            param1.ParameterName = "@identityId";
                            param2.ParameterName = "@identity";
                            param3.ParameterName = "@driveId";
                            param4.ParameterName = "@modified";

                            param1.Value = _identityId.ToByteArray();
                            param2.Value = _identity;
                            param3.Value = _driveId.ToByteArray();
                            param4.Value = _modified;

                            updateCmd.Parameters.Add(param1);
                            updateCmd.Parameters.Add(param2);
                            updateCmd.Parameters.Add(param3);
                            updateCmd.Parameters.Add(param4);

                            int n = await updateCmd.ExecuteNonQueryAsync();
                            countModified += n;
                        }
                    }
                    Console.WriteLine($"FollowsMe Created records fixed = {countCreated}");
                    Console.WriteLine($"FollowsMe Modified records fixed = {countModified}");
                }

                await FixTimes(cnOrg, "followsMe");
            } // followsMe

            // ImFollowing
            using (var selectCmd = cnOrg.CreateCommand())
            {
                selectCmd.CommandText = @$"
                    SELECT created,modified,identityId,identity,driveId
                    FROM original.Imfollowing
                    ORDER BY created ASC;";

                using (var rdr = await selectCmd.ExecuteReaderAsync(CommandBehavior.Default))
                {
                    long _created;
                    long? _modified;
                    Guid _identityId;
                    string _identity;
                    Guid _driveId;

                    int countCreated = 0;
                    int countModified = 0;
                    while (await rdr.ReadAsync())
                    {
                        _created = (long)rdr[0];
                        _modified = (rdr[1] == DBNull.Value) ? null : (long)rdr[1];
                        _identityId = new Guid((byte[])rdr[2]);
                        _identity = (string)rdr[3];
                        _driveId = new Guid((byte[])rdr[4]);

                        using (var updateCmd = cnOrg.CreateCommand())
                        {
                            updateCmd.CommandText = @$"
                                UPDATE Imfollowing
                                SET created = @created
                                WHERE identityId=@identityId AND identity=@identity AND driveId = @driveId";

                            var param1 = updateCmd.CreateParameter();
                            var param2 = updateCmd.CreateParameter();
                            var param3 = updateCmd.CreateParameter();
                            var param4 = updateCmd.CreateParameter();

                            param1.ParameterName = "@identityId";
                            param2.ParameterName = "@identity";
                            param3.ParameterName = "@driveId";
                            param4.ParameterName = "@created";

                            param1.Value = _identityId.ToByteArray();
                            param2.Value = _identity;
                            param3.Value = _driveId.ToByteArray();
                            param4.Value = _created;

                            updateCmd.Parameters.Add(param1);
                            updateCmd.Parameters.Add(param2);
                            updateCmd.Parameters.Add(param3);
                            updateCmd.Parameters.Add(param4);

                            int n = await updateCmd.ExecuteNonQueryAsync();

                            countCreated += n;
                        }

                        using (var updateCmd = cnOrg.CreateCommand())
                        {
                            if (_modified == null)
                                continue;

                            updateCmd.CommandText = @$"
                                UPDATE Imfollowing
                                SET modified = @modified
                                WHERE identityId=@identityId AND identity=@identity AND driveId = @driveId AND modified IS NULL";

                            var param1 = updateCmd.CreateParameter();
                            var param2 = updateCmd.CreateParameter();
                            var param3 = updateCmd.CreateParameter();
                            var param4 = updateCmd.CreateParameter();

                            param1.ParameterName = "@identityId";
                            param2.ParameterName = "@identity";
                            param3.ParameterName = "@driveId";
                            param4.ParameterName = "@modified";

                            param1.Value = _identityId.ToByteArray();
                            param2.Value = _identity;
                            param3.Value = _driveId.ToByteArray();
                            param4.Value = _modified;

                            updateCmd.Parameters.Add(param1);
                            updateCmd.Parameters.Add(param2);
                            updateCmd.Parameters.Add(param3);
                            updateCmd.Parameters.Add(param4);

                            int n = await updateCmd.ExecuteNonQueryAsync();
                            countModified += n;
                        }
                    }
                    Console.WriteLine($"ImFollowing Created records fixed = {countCreated}");
                    Console.WriteLine($"ImFollowing Modified records fixed = {countModified}");
                }

                await FixTimes(cnOrg, "Imfollowing");
            } // ImFollowing

            // Inbox
            using (var selectCmd = cnOrg.CreateCommand())
            {
                selectCmd.CommandText = @$"
                    SELECT created,modified,identityId,fileId
                    FROM original.Inbox
                    ORDER BY created ASC;";

                using (var rdr = await selectCmd.ExecuteReaderAsync(CommandBehavior.Default))
                {
                    long _created;
                    long? _modified;
                    Guid _identityId;
                    Guid _fileId;

                    int countCreated = 0;
                    int countModified = 0;
                    while (await rdr.ReadAsync())
                    {
                        _created = (long)rdr[0];
                        _modified = (rdr[1] == DBNull.Value) ? null : (long)rdr[1];
                        _identityId = new Guid((byte[])rdr[2]);
                        _fileId = new Guid((byte[])rdr[3]);

                        using (var updateCmd = cnOrg.CreateCommand())
                        {
                            updateCmd.CommandText = @$"
                                UPDATE Inbox
                                SET created = @created
                                WHERE identityId=@identityId AND fileId=@fileId";

                            var param1 = updateCmd.CreateParameter();
                            var param2 = updateCmd.CreateParameter();
                            var param4 = updateCmd.CreateParameter();

                            param1.ParameterName = "@identityId";
                            param2.ParameterName = "@fileId";
                            param4.ParameterName = "@created";

                            param1.Value = _identityId.ToByteArray();
                            param2.Value = _fileId.ToByteArray();
                            param4.Value = _created;

                            updateCmd.Parameters.Add(param1);
                            updateCmd.Parameters.Add(param2);
                            updateCmd.Parameters.Add(param4);

                            int n = await updateCmd.ExecuteNonQueryAsync();

                            countCreated += n;
                        }

                        using (var updateCmd = cnOrg.CreateCommand())
                        {
                            if (_modified == null)
                                continue;

                            updateCmd.CommandText = @$"
                                UPDATE Inbox
                                SET modified = @modified
                                WHERE identityId=@identityId AND fileId=@fileId AND modified IS NULL";

                            var param1 = updateCmd.CreateParameter();
                            var param2 = updateCmd.CreateParameter();
                            var param4 = updateCmd.CreateParameter();

                            param1.ParameterName = "@identityId";
                            param2.ParameterName = "@fileId";
                            param4.ParameterName = "@modified";

                            param1.Value = _identityId.ToByteArray();
                            param2.Value = _fileId.ToByteArray();
                            param4.Value = _modified;

                            updateCmd.Parameters.Add(param1);
                            updateCmd.Parameters.Add(param2);
                            updateCmd.Parameters.Add(param4);

                            int n = await updateCmd.ExecuteNonQueryAsync();
                            countModified += n;
                        }
                    }
                    Console.WriteLine($"Inbox Created records fixed = {countCreated}");
                    Console.WriteLine($"Inbox Modified records fixed = {countModified}");
                }

                await FixTimes(cnOrg, "Inbox");
            } // Inbox



        }
        finally
        {
            // Detach the backup database from the main connection
            using (var detachCmd = cnOrg.CreateCommand())
            {
                detachCmd.CommandText = "DETACH DATABASE original";
                await detachCmd.ExecuteNonQueryAsync();
            }
        }
    }
}
