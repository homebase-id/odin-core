using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory.Sqlite;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Migration.DriveAliasPhase1;

public static class DriveAliasMigrationPhase2
{
    public static async Task MigrateData(IIdentityRegistry registry, MultiTenantContainer tenantContainer, ILogger logger)
    {
        var allTenants = await registry.GetTenants();
        foreach (var tenant in allTenants)
        {
            logger.LogInformation("Drive Migration started for tenant {tenant}", tenant.PrimaryDomainName);
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var tenantContext = scope.Resolve<TenantContext>();
            var scopedIdentityTransactionFactory = scope.Resolve<ScopedIdentityTransactionFactory>();
            
            //TODO: decide on how im passing connections, etc. etc
            // await MigrateData(tenantId, connectionString);

        }
    }

    private static async Task MigrateData(Guid tenantId, string connectionString)
    {
        await Task.CompletedTask;
        // Migrate the data
        // await using var cn = await SqliteConcreteConnectionFactory.CreateAsync(connectionString);
        // await using var tx = await cn.BeginTransactionAsync();
        //
        // var getDrivesCommand = cn.CreateCommand();
        //
        // getDrivesCommand.CommandText = @"SELECT 
        //                                    key1 as driveId, 
        //                                    data as json 
        //                                FROM KeyThreeValue 
        //                                WHERE identityId = @identityId 
        //                                AND LOWER(key3) = 'drive'";
        //
        // var identityId = tenantId;
        // var identityParam = getDrivesCommand.CreateParameter();
        // identityParam.ParameterName = "@identityId";
        // identityParam.Value = identityId.ToByteArray();
        // getDrivesCommand.Parameters.Add(identityParam);
        //
        // var drives = new List<(Guid driveId, string json)>();
        // await using (var rdr = await getDrivesCommand.ExecuteReaderAsync(CommandBehavior.Default))
        // {
        //     while (await rdr.ReadAsync())
        //     {
        //         drives.Add((rdr.GetGuid(0), rdr.GetString(1)));
        //     }
        // }
        //
        // foreach (var item in drives)
        // {
        //     var json = item.json;
        //     var storageDrive = OdinSystemSerializer.Deserialize<StorageDriveBaseForMigration>(json);
        //     InsertStorageDrive(cn, identityId, storageDrive).GetAwaiter().GetResult();
        //
        //     var oldDriveId = storageDrive.Id;
        //     var newDriveId = storageDrive.TargetDriveInfo.Alias;
        //
        //     MigrateDriveMainIndex(cn, identityId, oldDriveId, newDriveId).GetAwaiter().GetResult();
        //     MigrateDriveLocalTagIndex(cn, identityId, oldDriveId, newDriveId).GetAwaiter().GetResult();
        //     MigrateDriveAclIndex(cn, identityId, oldDriveId, newDriveId).GetAwaiter().GetResult();
        //     MigrateDriveTransferHistory(cn, identityId, oldDriveId, newDriveId).GetAwaiter().GetResult();
        //     MigrateDriveReactions(cn, identityId, oldDriveId, newDriveId).GetAwaiter().GetResult();
        // }
        //
        // //validate no tables have the old driveId
        //
        // await tx.CommitAsync();
    }
    
    private static async Task InsertStorageDrive(DbConnection cn, Guid identityId, StorageDrive storageDrive)
    {
        var driveId = storageDrive.TargetDriveInfo.Alias.Value;

        await using var insertCommand = cn.CreateCommand();
        insertCommand.CommandText =
            "INSERT INTO DriveDefinitions (identityId, driveId, driveType, data) VALUES (@identityId, @driveId, @driveType, @data);";

        var p1 = insertCommand.CreateParameter();
        p1.ParameterName = "@identityId";
        insertCommand.Parameters.Add(p1);
        p1.Value = identityId.ToByteArray();

        var p2 = insertCommand.CreateParameter();
        p2.ParameterName = "@driveId";
        insertCommand.Parameters.Add(p2);
        p2.Value = driveId.ToByteArray();

        var p3 = insertCommand.CreateParameter();
        p3.ParameterName = "@driveType";
        insertCommand.Parameters.Add(p3);
        p3.Value = storageDrive.TargetDriveInfo.Type.Value.ToByteArray();

        var p4 = insertCommand.CreateParameter();
        p4.ParameterName = "@data";
        insertCommand.Parameters.Add(p4);
        p4.Value = OdinSystemSerializer.Serialize(storageDrive);

        var count = await insertCommand.ExecuteNonQueryAsync();
    }

    private static async Task MigrateDriveMainIndex(DbConnection cn, Guid identityId, Guid oldDriveId, Guid newDriveId)
    {
        await using var command = cn.CreateCommand();
        command.CommandText = "UPDATE driveMainIndex SET driveId = @newDriveId WHERE identityId = @identityId AND @driveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = newDriveId.ToByteArray();

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();

        var p3 = command.CreateParameter();
        p3.ParameterName = "@driveId";
        p3.Value = oldDriveId.ToByteArray();

        await command.ExecuteNonQueryAsync();
        await AssertUpdateSuccess(cn, "driveMainIndex", identityId, oldDriveId);
    }

    private static async Task MigrateDriveLocalTagIndex(DbConnection cn, Guid identityId, Guid oldDriveId, Guid newDriveId)
    {
        await using var command = cn.CreateCommand();
        command.CommandText =
            "UPDATE DriveLocalTagIndex SET driveId = @newDriveId WHERE identityId = @identityId AND @driveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = newDriveId.ToByteArray();

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();

        var p3 = command.CreateParameter();
        p3.ParameterName = "@driveId";
        p3.Value = oldDriveId.ToByteArray();

        await command.ExecuteNonQueryAsync();

        await AssertUpdateSuccess(cn, "driveMainIndex", identityId, oldDriveId);
    }

    private static async Task MigrateDriveAclIndex(DbConnection cn, Guid identityId, Guid oldDriveId, Guid newDriveId)
    {
        await using var command = cn.CreateCommand();
        command.CommandText = "UPDATE DriveAclIndex SET driveId = @newDriveId WHERE identityId = @identityId AND @driveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = newDriveId.ToByteArray();

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();

        var p3 = command.CreateParameter();
        p3.ParameterName = "@driveId";
        p3.Value = oldDriveId.ToByteArray();

        await command.ExecuteNonQueryAsync();

        await AssertUpdateSuccess(cn, "driveMainIndex", identityId, oldDriveId);
    }

    private static async Task MigrateDriveTransferHistory(DbConnection cn, Guid identityId, Guid oldDriveId, Guid newDriveId)
    {
        await using var command = cn.CreateCommand();
        command.CommandText =
            "UPDATE DriveTransferHistory SET driveId = @newDriveId WHERE identityId = @identityId AND @driveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = newDriveId.ToByteArray();

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();

        var p3 = command.CreateParameter();
        p3.ParameterName = "@driveId";
        p3.Value = oldDriveId.ToByteArray();

        await command.ExecuteNonQueryAsync();

        await AssertUpdateSuccess(cn, "driveMainIndex", identityId, oldDriveId);
    }

    private static async Task MigrateDriveReactions(DbConnection cn, Guid identityId, Guid oldDriveId, Guid newDriveId)
    {
        await using var command = cn.CreateCommand();
        command.CommandText = "UPDATE DriveReactions SET driveId = @newDriveId WHERE identityId = @identityId AND @driveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = newDriveId.ToByteArray();

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();

        var p3 = command.CreateParameter();
        p3.ParameterName = "@driveId";
        p3.Value = oldDriveId.ToByteArray();

        await command.ExecuteNonQueryAsync();

        await AssertUpdateSuccess(cn, "driveMainIndex", identityId, oldDriveId);
    }

    private static async Task AssertUpdateSuccess(DbConnection cn, string tableName, Guid identityId, Guid oldDriveId)
    {
        await using var validateCommand = cn.CreateCommand();
        validateCommand.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE identityId = @identityId AND @driveId = @oldDriveId";

        var v1 = validateCommand.CreateParameter();
        v1.ParameterName = "@identityId";
        v1.Value = identityId.ToByteArray();

        var v2 = validateCommand.CreateParameter();
        v2.ParameterName = "@driveId";
        v2.Value = oldDriveId.ToByteArray();

        var count = await validateCommand.ExecuteScalarAsync();
        if (Convert.ToInt32(count) > 0)
        {
            throw new OdinSystemException(
                $"Found {Convert.ToInt32(count)} rows remaining in table {tableName} for old driveId {oldDriveId} on identityId {identityId}");
        }
    }
}