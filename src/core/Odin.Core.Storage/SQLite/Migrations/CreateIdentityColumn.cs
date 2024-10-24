#if DAPPER
using System;
using System.IO;
using Dapper;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.SQLite.Migrations;

public static class CreateIdentityColumn
{
    // sudo find . -type f -name 'sys.db' -execdir bash -c 'if [ ! -f identity.db ]; then mv "{}" identity.db; else rm "{}"; fi' \;

    // sudo find . -type f -name '*.db-shm' -execdir sudo rm {} \;
    // sudo find . -type f -name '*.db-wal' -execdir sudo rm {} \;

    // docker exec identity-host dotnet Odin.Hosting.dll --create-identity-column

    //
    // pushd src/apps/Odin.Hosting
    // dotnet clean && dotnet publish -r osx-x64 --self-contained -p:PublishSingleFile=true
    // OR
    // dotnet clean && dotnet publish -r linux-x64 --self-contained -p:PublishSingleFile=true
    //
    // pushd src/apps/Odin.Hosting/bin/Release/net8.0/osx-x64/publish/
    // OR
    // pushd src/apps/Odin.Hosting/bin/Release/net8.0/linux-x64/publish/
    // sudo cp /identity-host/config/appsettings.production.json .
    // sudo chmod a+rw appsettings.production.json
    //
    // ASPNETCORE_ENVIRONMENT=production ./Odin.Hosting --create-identity-column
    // OR
    // ASPNETCORE_ENVIRONMENT=development ./Odin.Hosting --create-identity-column
    //

    //
    // after migration:
    // find . -type f -name "identity.db" -exec rm -f {} +
    // find . -type f -name "newidentity.db" -execdir mv {} sys.db \;
    //

    public static void Execute(string tenantDataRootPath)
    {
        DapperExtensions.ConfigureTypeHandlers();

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
        var newDbPath = Path.Combine(tenantDir, "headers", "newidentity.db");

        if (!File.Exists(orgDbPath))
        {
            throw new Exception("Database not found: " + orgDbPath);
        }
        
        if (File.Exists(oldDbPath)) File.Delete(oldDbPath);
        BackupSqliteDatabase.Execute(orgDbPath, oldDbPath);

        using var oldDb = new IdentityDatabase.IdentityDatabase(tenantId, oldDbPath);
        using var oldCn = oldDb.CreateDisposableConnection();
        using var newDb = new IdentityDatabase.IdentityDatabase(tenantId, newDbPath);
        using var newCn = newDb.CreateDisposableConnection();

        // DriveMainIndex
        {
            Console.WriteLine("  DriveMainIndex");
            
            // Clean the source table
            oldCn.Connection.Execute(
                $"""
                 UPDATE {oldDb.tblDriveMainIndex._tableName}
                 SET senderId = NULL
                 WHERE senderId = '' OR senderId = 'System.Byte[]' OR senderId = x'';
                 """);
            
            newDb.tblDriveMainIndex.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<DriveMainIndexRecord>($"SELECT * FROM {newDb.tblDriveMainIndex._tableName} order by created asc");
            foreach (var record in records)
            {
                var created = record.created;
                var modified = record.modified;

                record.identityId = tenantId;
                newDb.tblDriveMainIndex.Insert(record);

                // Recreate values 'created' and 'modified'
                newCn.Connection.Execute(
                    $"""
                    UPDATE {newDb.tblDriveMainIndex._tableName}
                    SET created = @created, modified = @modified
                    WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId;
                    """,
                    new
                    {
                     created,
                     modified,
                     identityId = tenantId.ToByteArray(),
                     driveId = record.driveId.ToByteArray(),
                     fileId = record.fileId.ToByteArray()
                    });
            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblDriveMainIndex._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // DriveAclIndex
        {
            Console.WriteLine("  DriveAclIndex");
            newDb.tblDriveAclIndex.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<DriveAclIndexRecord>($"SELECT * FROM {newDb.tblDriveAclIndex._tableName}");
            foreach (var record in records)
            {
                record.identityId = tenantId;
                newDb.tblDriveAclIndex.Insert(record);
            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblDriveAclIndex._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // DriveTagIndex
        {
            Console.WriteLine("  DriveTagIndex");
            newDb.tblDriveTagIndex.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<DriveTagIndexRecord>($"SELECT * FROM {newDb.tblDriveTagIndex._tableName}");
            foreach (var record in records)
            {
                record.identityId = tenantId;
                newDb.tblDriveTagIndex.Insert(record);
            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblDriveTagIndex._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // DriveReactions
        {
            Console.WriteLine("  DriveReactions");
            newDb.tblDriveReactions.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<DriveReactionsRecord>($"SELECT * FROM {newDb.tblDriveReactions._tableName}");
            foreach (var record in records)
            {
                record.identityId = tenantId;
                newDb.tblDriveReactions.Insert(newCn, record);
            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblDriveReactions._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // AppGrants
        {
            Console.WriteLine("  AppGrants");
            newDb.tblAppGrants.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<AppGrantsRecord>($"SELECT * FROM {newDb.tblAppGrants._tableName}");
            foreach (var record in records)
            {
                record.identityId = tenantId;
                newDb.tblAppGrants.Insert(record);
            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblAppGrants._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // KeyValue
        {
            Console.WriteLine("  KeyValue");
            newDb.tblKeyValue.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<KeyValueRecord>($"SELECT * FROM {newDb.tblKeyValue._tableName}");
            foreach (var record in records)
            {
                record.identityId = tenantId;
                newDb.tblKeyValue.Insert(record);
            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblKeyValue._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // KeyTwoValue
        {
            Console.WriteLine("  KeyTwoValue");
            newDb.tblKeyTwoValue.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<KeyTwoValueRecord>($"SELECT * FROM {newDb.tblKeyTwoValue._tableName}");
            foreach (var record in records)
            {
                record.identityId = tenantId;
                newDb.tblKeyTwoValue.Insert(record);
            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblKeyTwoValue._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // KeyThreeValue
        {
            Console.WriteLine("  KeyThreeValue");
            newDb.TblKeyThreeValue.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<KeyThreeValueRecord>($"SELECT * FROM {newDb.TblKeyThreeValue._tableName}");
            foreach (var record in records)
            {
                record.identityId = tenantId;
                newDb.TblKeyThreeValue.Insert(record);
            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.TblKeyThreeValue._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // Inbox
        {
            Console.WriteLine("  Inbox");
            newDb.tblInbox.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<InboxRecord>($"SELECT * FROM {newDb.tblInbox._tableName}");
            foreach (var record in records)
            {
                var created = record.created;
                var modified = record.modified;

                record.identityId = tenantId;
                newDb.tblInbox.Insert(record);

                // Recreate values 'created' and 'modified'
                newCn.Connection.Execute(
                    $"""
                     UPDATE {newDb.tblInbox._tableName}
                     SET created = @created, modified = @modified
                     WHERE identityId = @identityId AND fileId = @fileId;
                     """,
                    new
                    {
                        created,
                        modified,
                        identityId = tenantId.ToByteArray(),
                        fileId = record.fileId.ToByteArray()
                    });
            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblInbox._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // Outbox
        {
            Console.WriteLine("  Outbox");
            newDb.tblOutbox.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<OutboxRecord>($"SELECT * FROM {newDb.tblOutbox._tableName}");
            foreach (var record in records)
            {
                var created = record.created;
                var modified = record.modified;

                record.identityId = tenantId;
                newDb.tblOutbox.Insert(record);

                // Recreate values 'created' and 'modified'
                newCn.Connection.Execute(
                    $"""
                     UPDATE {newDb.tblOutbox._tableName}
                     SET created = @created, modified = @modified
                     WHERE identityId = @identityId AND fileId = @fileId AND recipient = @recipient;
                     """,
                    new
                    {
                        created,
                        modified,
                        identityId = tenantId.ToByteArray(),
                        driveId = record.driveId.ToByteArray(),
                        fileId = record.fileId.ToByteArray(),
                        recipient = record.recipient
                    });

            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblOutbox._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // Circle
        {
            Console.WriteLine("  Circle");
            newDb.tblCircle.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<CircleRecord>($"SELECT * FROM {newDb.tblCircle._tableName}");
            foreach (var record in records)
            {
                record.identityId = tenantId;
                newDb.tblCircle.Insert(record);
            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblCircle._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // CircleMember
        {
            Console.WriteLine("  CircleMember");
            newDb.tblCircleMember.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<CircleMemberRecord>($"SELECT * FROM {newDb.tblCircleMember._tableName}");
            foreach (var record in records)
            {
                record.identityId = tenantId;
                newDb.tblCircleMember.Insert(record);
            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblCircleMember._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // FollowsMe
        {
            Console.WriteLine("  FollowsMe");
            newDb.tblFollowsMe.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<FollowsMeRecord>($"SELECT * FROM {newDb.tblFollowsMe._tableName}");
            foreach (var record in records)
            {
                var created = record.created;
                var modified = record.modified;

                record.identityId = tenantId;
                newDb.tblFollowsMe.Insert(record);

                // Recreate values 'created' and 'modified'
                newCn.Connection.Execute(
                    $"""
                     UPDATE {newDb.tblFollowsMe._tableName}
                     SET created = @created, modified = @modified
                     WHERE identityId = @identityId AND identity = @identity AND driveId = @driveId;
                     """,
                    new
                    {
                        created,
                        modified,
                        identityId = tenantId.ToByteArray(),
                        identity = record.identity,
                        driveId = record.driveId.ToByteArray(),
                    });

            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblFollowsMe._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // ImFollowing
        {
            Console.WriteLine("  ImFollowing");
            newDb.tblImFollowing.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<ImFollowingRecord>($"SELECT * FROM {newDb.tblImFollowing._tableName}");
            foreach (var record in records)
            {
                var created = record.created;
                var modified = record.modified;

                record.identityId = tenantId;
                newDb.tblImFollowing.Insert(record);

                // Recreate values 'created' and 'modified'
                newCn.Connection.Execute(
                    $"""
                     UPDATE {newDb.tblImFollowing._tableName}
                     SET created = @created, modified = @modified
                     WHERE identityId = @identityId AND identity = @identity AND driveId = @driveId;
                     """,
                    new
                    {
                        created,
                        modified,
                        identityId = tenantId.ToByteArray(),
                        identity = record.identity.ToString(),
                        driveId = record.driveId.ToByteArray(),
                    });

            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblImFollowing._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // Connections
        {
            Console.WriteLine("  Connections");
            newDb.tblConnections.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<ConnectionsRecord>($"SELECT * FROM {newDb.tblConnections._tableName}");
            foreach (var record in records)
            {
                var created = record.created;
                var modified = record.modified;

                record.identityId = tenantId;
                newDb.tblConnections.Insert(record);

                // Recreate values 'created' and 'modified'
                newCn.Connection.Execute(
                    $"""
                     UPDATE {newDb.tblConnections._tableName}
                     SET created = @created, modified = @modified
                     WHERE identityId = @identityId AND identity = @identity;
                     """,
                    new
                    {
                        created,
                        modified,
                        identityId = tenantId.ToByteArray(),
                        identity = record.identity.ToString(),
                    });

            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblConnections._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        // AppNotificationsTable
        {
            Console.WriteLine("  AppNotificationsTable");
            
            // Clean the source table
            oldCn.Connection.Execute(
                $"""
                 UPDATE {newDb.tblAppNotificationsTable._tableName}
                 SET senderId = NULL
                 WHERE senderId = '' OR senderId = 'System.Byte[]' OR senderId = x'';
                 """);
            
            newDb.tblAppNotificationsTable.EnsureTableExists(newCn, true);
            var records = oldCn.Connection.Query<AppNotificationsRecord>($"SELECT * FROM {newDb.tblAppNotificationsTable._tableName}");
            foreach (var record in records)
            {
                var created = record.created;
                var modified = record.modified;

                record.identityId = tenantId;
                newDb.tblAppNotificationsTable.Insert(record);

                // Recreate values 'created' and 'modified'
                newCn.Connection.Execute(
                    $"""
                     UPDATE {newDb.tblAppNotificationsTable._tableName}
                     SET created = @created, modified = @modified
                     WHERE identityId = @identityId AND notificationId = @notificationId;
                     """,
                    new
                    {
                        created,
                        modified,
                        identityId = tenantId.ToByteArray(),
                        notificationId = record.notificationId.ToByteArray(),
                    });

            }

            var sqlCount = $"SELECT COUNT(*) FROM {newDb.tblAppNotificationsTable._tableName}";
            var oldCount = oldCn.Connection.ExecuteScalar<int>(sqlCount);
            var newCount = newCn.Connection.ExecuteScalar<int>(sqlCount);
            if (oldCount != newCount)
            {
                throw new Exception($"oldCount != newCount: {oldCount} != {newCount}");
            }

        }

        newCn.Connection.Query("PRAGMA integrity_check");
    }
}
#endif