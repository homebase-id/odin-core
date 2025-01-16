using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Odin.Core.Serialization;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.SQLite.Migrations.TransferHistory;

public static class TransferHistoryMigration
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
        var oldDbPath = Path.Combine(tenantDir, "headers", "oldidentity-pre-local-app-metadata.db");
        // var newDbPath = Path.Combine(tenantDir, "headers", "newidentity.db");

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


        PrepareSchema(connectionString).GetAwaiter().GetResult();
        MigrateData(tenantId, connectionString).GetAwaiter().GetResult();
    }

    private static async Task PrepareSchema(string connectionString)
    {
        await using var cn = await SqliteConcreteConnectionFactory.CreateAsync(connectionString);

        // Create table
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS driveTransferHistory("
                + "identityId BYTEA NOT NULL, "
                + "driveId BYTEA NOT NULL, "
                + "fileId BYTEA NOT NULL, "
                + "remoteIdentityId TEXT NOT NULL, "
                + "latestTransferStatus BIGINT , "
                + "isInOutbox BIGINT , "
                + "latestSuccessfullyDeliveredVersionTag BYTEA , "
                + "isReadByRecipient BIGINT  "
                + ", PRIMARY KEY (identityId,driveId,fileId,remoteIdentityId)"
                + ");"
                + "CREATE INDEX IF NOT EXISTS Idx0TableDriveTransferHistoryCRUD ON driveTransferHistory(identityId,driveId,fileId);";

            await cmd.ExecuteNonQueryAsync();
        }

        // Alter table
        // {
        //     await using var cmd1 = cn.CreateCommand();
        //     cmd1.CommandText = "ALTER TABLE ...";
        //     await cmd1.ExecuteNonQueryAsync();
        // }
    }

    private static async Task MigrateData(Guid tenantId, string connectionString)
    {
        // Migrate the data
        await using var cn = await SqliteConcreteConnectionFactory.CreateAsync(connectionString);

        // get the existing transfer history, if not null; parse and insert into transfer history table

        var getTransferHistoryCommand = cn.CreateCommand();
        getTransferHistoryCommand.CommandText = "SELECT driveId, fileId, hdrTransferHistory FROM driveMainIndex " +
                                                "WHERE identityId = @identityId " +
                                                "AND hdrTransferHistory IS NOT NULL";

        var identityParam = getTransferHistoryCommand.CreateParameter();
        identityParam.ParameterName = "@identityId";
        identityParam.Value = tenantId.ToByteArray();

        //TODO: need a transaction here

        using (var rdr = await getTransferHistoryCommand.ExecuteReaderAsync(CommandBehavior.Default))
        {
            while (await rdr.ReadAsync())
            {
                var driveId = new Guid((byte[])rdr[0]);
                var fileId = new Guid((byte[])rdr[1]);
                var transferHistory = OdinSystemSerializer.Deserialize<RecipientTransferHistoryForMigration>((string)rdr[2]);

                await InsertDriveTransferHistory(cn, tenantId, driveId, fileId, transferHistory);

                await CreateSummary(cn, tenantId, driveId, fileId);
            }
        }
    }

    private static async Task CreateSummary(DbConnection cn, Guid tenantId, Guid driveId, Guid fileId)
    {
        var summary = new TransferHistorySummary();

        var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT latestTransferStatus, count(*) FROM driveTransferHistory " +
                          "WHERE identityId = @identityId " +
                          "AND driveId = @driveId " +
                          "AND fileId = @fileId";

        var identityParam = cmd.CreateParameter();
        identityParam.ParameterName = "@identityId";
        identityParam.Value = tenantId.ToByteArray();

        var driveIdParam = cmd.CreateParameter();
        driveIdParam.ParameterName = "@driveId";
        driveIdParam.Value = driveId.ToByteArray();

        var fileIdParam = cmd.CreateParameter();
        fileIdParam.ParameterName = "@fileId";
        fileIdParam.Value = fileId.ToByteArray();

        var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            // LatestTransferStatusForMigration status = (LatestTransferStatusForMigration)(int)rdr[0];
            int status = (int)rdr[0];
            var count = (int)rdr[1];
            summary.Items[status] = count;

            // var name = Enum.GetName(typeof(LatestTransferStatusForMigration), status);
            // summary.Items[name] = count;
        }
    }

    private static async Task InsertDriveTransferHistory(DbConnection cn, Guid identityId, Guid driveId, Guid fileId,
        RecipientTransferHistoryForMigration transferHistoryForMigration)
    {
        foreach (var recipient in transferHistoryForMigration.Recipients)
        {
            var remoteIdentity = recipient.Key;
            var item = recipient.Value;

            await InsertTransferHistoryRecord(cn, identityId, driveId, fileId, remoteIdentity, item);
        }
    }

    private static async Task InsertTransferHistoryRecord(DbConnection cn, Guid identityId, Guid driveId, Guid fileId,
        string remoteIdentity,
        RecipientTransferHistoryItemForMigration item)
    {
        await using var upsertCommand = cn.CreateCommand();
        upsertCommand.CommandText =
            "INSERT INTO driveTransferHistory (identityId,driveId,fileId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient) " +
            "VALUES (@identityId,@driveId,@fileId,@remoteIdentityId,@latestTransferStatus,@isInOutbox,@latestSuccessfullyDeliveredVersionTag,@isReadByRecipient);";
        var upsertParam1 = upsertCommand.CreateParameter();
        upsertParam1.ParameterName = "@identityId";
        upsertCommand.Parameters.Add(upsertParam1);
        var upsertParam2 = upsertCommand.CreateParameter();
        upsertParam2.ParameterName = "@driveId";
        upsertCommand.Parameters.Add(upsertParam2);
        var upsertParam3 = upsertCommand.CreateParameter();
        upsertParam3.ParameterName = "@fileId";
        upsertCommand.Parameters.Add(upsertParam3);
        var upsertParam4 = upsertCommand.CreateParameter();
        upsertParam4.ParameterName = "@remoteIdentityId";
        upsertCommand.Parameters.Add(upsertParam4);
        var upsertParam5 = upsertCommand.CreateParameter();
        upsertParam5.ParameterName = "@latestTransferStatus";
        upsertCommand.Parameters.Add(upsertParam5);
        var upsertParam6 = upsertCommand.CreateParameter();
        upsertParam6.ParameterName = "@isInOutbox";
        upsertCommand.Parameters.Add(upsertParam6);
        var upsertParam7 = upsertCommand.CreateParameter();
        upsertParam7.ParameterName = "@latestSuccessfullyDeliveredVersionTag";
        upsertCommand.Parameters.Add(upsertParam7);
        var upsertParam8 = upsertCommand.CreateParameter();
        upsertParam8.ParameterName = "@isReadByRecipient";
        upsertCommand.Parameters.Add(upsertParam8);
        upsertParam1.Value = identityId.ToByteArray();
        upsertParam2.Value = driveId.ToByteArray();
        upsertParam3.Value = fileId.ToByteArray();
        upsertParam4.Value = remoteIdentity;
        upsertParam5.Value = (int)item.LatestTransferStatus;
        upsertParam6.Value = item.IsInOutbox;
        upsertParam7.Value = item.LatestSuccessfullyDeliveredVersionTag?.ToByteArray() ?? (object)DBNull.Value;
        upsertParam8.Value = item.IsReadByRecipient;
        var count = await upsertCommand.ExecuteNonQueryAsync();
    }
}