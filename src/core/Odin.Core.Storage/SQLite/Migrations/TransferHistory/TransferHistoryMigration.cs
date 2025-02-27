using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Factory.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.Migrations.TransferHistory;

public static class TransferHistoryMigration
{
    public static void Execute(string tenantDataRootPath)
    {
        var tenantDirs = Directory.GetDirectories(Path.Combine(tenantDataRootPath, "registrations"));
        foreach (var tenantDir in tenantDirs)
        {
            MigrateDatabase(tenantDir);
        }
    }

    //

    private static void MigrateDatabase(string tenantDir)
    {
        Console.WriteLine(tenantDir);
        var tenantId = Guid.Parse(Path.GetFileName(tenantDir));

        var orgDbPath = Path.Combine(tenantDir, "headers", "identity.db");
        var oldDbPath = Path.Combine(tenantDir, "headers", "oldidentity-pre-transfer-history-metadata.db");
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
        await using var tx = await cn.BeginTransactionAsync();

        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            "CREATE TABLE IF NOT EXISTS driveTransferHistory("
            + "identityId BYTEA NOT NULL, "
            + "driveId BYTEA NOT NULL, "
            + "fileId BYTEA NOT NULL, "
            + "remoteIdentityId TEXT NOT NULL, "
            + "latestTransferStatus BIGINT NOT NULL, "
            + "isInOutbox BOOLEAN NOT NULL, "
            + "latestSuccessfullyDeliveredVersionTag BYTEA , "
            + "isReadByRecipient BOOLEAN NOT NULL "
            + ", PRIMARY KEY (identityId,driveId,fileId,remoteIdentityId)"
            + ");"
            + "CREATE INDEX IF NOT EXISTS Idx0TableDriveTransferHistoryCRUD ON driveTransferHistory(identityId,driveId,fileId);"
            ;

        await cmd.ExecuteNonQueryAsync();

        // Alter table
        // {
        //     await using var cmd1 = cn.CreateCommand();
        //     cmd1.CommandText = "ALTER TABLE ...";
        //     await cmd1.ExecuteNonQueryAsync();
        // }

        await tx.CommitAsync();
    }

    private static async Task MigrateData(Guid tenantId, string connectionString)
    {
        // Migrate the data
        await using var cn = await SqliteConcreteConnectionFactory.CreateAsync(connectionString);
        await using var tx = await cn.BeginTransactionAsync();

        await FixNullStringValueInDb(cn);

        var getTransferHistoryCommand = cn.CreateCommand();
        getTransferHistoryCommand.CommandText = "SELECT driveId, fileId, hdrTransferHistory FROM driveMainIndex " +
                                                "WHERE identityId = @identityId " +
                                                "AND hdrTransferHistory IS NOT NULL";

        var identityParam = getTransferHistoryCommand.CreateParameter();
        identityParam.ParameterName = "@identityId";
        identityParam.Value = tenantId.ToByteArray();
        getTransferHistoryCommand.Parameters.Add(identityParam);

        var list = new List<(Guid driveId, Guid fileId, string json)>();
        using (var rdr = await getTransferHistoryCommand.ExecuteReaderAsync(CommandBehavior.Default))
        {
            while (await rdr.ReadAsync())
            {
                list.Add(
                    (rdr.GetGuid(0),
                        rdr.GetGuid(1),
                        rdr.GetString(2)));
            }
        }

        foreach (var item in list)
        {
            var driveId = item.driveId;
            var fileId = item.fileId;
            var json = item.json;
            var transferHistory = OdinSystemSerializer.Deserialize<RecipientTransferHistoryForMigrationOld>(json);
            await InsertDriveTransferHistory(cn, tenantId, driveId, fileId, transferHistory);
            await CreateSummary(cn, tenantId, driveId, fileId);
        }

        await tx.CommitAsync();
    }


    private static async Task FixNullStringValueInDb(DbConnection cn)
    {
        var getTransferHistoryCommand = cn.CreateCommand();
        getTransferHistoryCommand.CommandText = """
                                                UPDATE driveMainIndex SET hdrTransferHistory = NULL WHERE  hdrTransferHistory = 'null';
                                                """;

        await getTransferHistoryCommand.ExecuteNonQueryAsync();
    }

    private static async Task CreateSummary(DbConnection cn, Guid tenantId, Guid driveId, Guid fileId)
    {
        var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag," +
                          "isReadByRecipient FROM driveTransferHistory " +
                          "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId;";

        var identityParam = cmd.CreateParameter();
        identityParam.ParameterName = "@identityId";
        identityParam.Value = tenantId.ToByteArray();
        cmd.Parameters.Add(identityParam);

        var driveIdParam = cmd.CreateParameter();
        driveIdParam.ParameterName = "@driveId";
        driveIdParam.Value = driveId.ToByteArray();
        cmd.Parameters.Add(driveIdParam);

        var fileIdParam = cmd.CreateParameter();
        fileIdParam.ParameterName = "@fileId";
        fileIdParam.Value = fileId.ToByteArray();
        cmd.Parameters.Add(fileIdParam);

        var rdr = await cmd.ExecuteReaderAsync();
        var fileTransferHistory = new List<RecipientTransferHistoryItemForMigration>();

        while (await rdr.ReadAsync())
        {
            var item = new RecipientTransferHistoryItemForMigration()
            {
                Recipient = new OdinId((string)rdr[0]),
                LastUpdated = default,
                LatestTransferStatus = (LatestTransferStatusForMigration)(int)(long)rdr[1],
                IsInOutbox = rdr.IsDBNull(2) ? false : Convert.ToBoolean(rdr[2]),
                LatestSuccessfullyDeliveredVersionTag = rdr.IsDBNull(3) ? null : new Guid((byte[])rdr[3]),
                IsReadByRecipient = rdr.IsDBNull(4) ? false : Convert.ToBoolean(rdr[4]),
            };

            fileTransferHistory.Add(item);
        }

        // now summarize using code from long term storage manager
        var history = new RecipientTransferHistoryForMigration()
        {
            Summary = new TransferHistorySummaryForMigration()
            {
                TotalInOutbox = fileTransferHistory.Count(h => h.IsInOutbox),
                TotalFailed = fileTransferHistory.Count(h => h.LatestTransferStatus != LatestTransferStatusForMigration.Delivered &&
                                                             h.LatestTransferStatus != LatestTransferStatusForMigration.None),
                TotalDelivered = fileTransferHistory.Count(h => h.LatestTransferStatus == LatestTransferStatusForMigration.Delivered),
                TotalReadByRecipient = fileTransferHistory.Count(h => h.IsReadByRecipient)
            }
        };

        await UpdateTransferHistoryCache(cn, tenantId, driveId, fileId, OdinSystemSerializer.Serialize(history));
    }

    private static async Task InsertDriveTransferHistory(DbConnection cn, Guid identityId, Guid driveId, Guid fileId,
        RecipientTransferHistoryForMigrationOld transferHistoryForMigration)
    {
        foreach (var recipient in transferHistoryForMigration?.Recipients ?? [])
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
        await using var insertCommand = cn.CreateCommand();
        insertCommand.CommandText =
            "INSERT INTO driveTransferHistory (identityId,driveId,fileId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient) " +
            "VALUES (@identityId,@driveId,@fileId,@remoteIdentityId,@latestTransferStatus,@isInOutbox,@latestSuccessfullyDeliveredVersionTag,@isReadByRecipient);";
        var upsertParam1 = insertCommand.CreateParameter();
        upsertParam1.ParameterName = "@identityId";
        insertCommand.Parameters.Add(upsertParam1);
        var upsertParam2 = insertCommand.CreateParameter();
        upsertParam2.ParameterName = "@driveId";
        insertCommand.Parameters.Add(upsertParam2);
        var upsertParam3 = insertCommand.CreateParameter();
        upsertParam3.ParameterName = "@fileId";
        insertCommand.Parameters.Add(upsertParam3);
        var upsertParam4 = insertCommand.CreateParameter();
        upsertParam4.ParameterName = "@remoteIdentityId";
        insertCommand.Parameters.Add(upsertParam4);
        var upsertParam5 = insertCommand.CreateParameter();
        upsertParam5.ParameterName = "@latestTransferStatus";
        insertCommand.Parameters.Add(upsertParam5);
        var upsertParam6 = insertCommand.CreateParameter();
        upsertParam6.ParameterName = "@isInOutbox";
        insertCommand.Parameters.Add(upsertParam6);
        var upsertParam7 = insertCommand.CreateParameter();
        upsertParam7.ParameterName = "@latestSuccessfullyDeliveredVersionTag";
        insertCommand.Parameters.Add(upsertParam7);
        var upsertParam8 = insertCommand.CreateParameter();
        upsertParam8.ParameterName = "@isReadByRecipient";
        insertCommand.Parameters.Add(upsertParam8);
        upsertParam1.Value = identityId.ToByteArray();
        upsertParam2.Value = driveId.ToByteArray();
        upsertParam3.Value = fileId.ToByteArray();
        upsertParam4.Value = remoteIdentity;
        upsertParam5.Value = (int)item.LatestTransferStatus;
        upsertParam6.Value = item.IsInOutbox;
        upsertParam7.Value = item.LatestSuccessfullyDeliveredVersionTag?.ToByteArray() ?? (object)DBNull.Value;
        upsertParam8.Value = item.IsReadByRecipient;
        var count = await insertCommand.ExecuteNonQueryAsync();
    }

    public static async Task<int> UpdateTransferHistoryCache(DbConnection cn, Guid tenantId, Guid driveId, Guid fileId,
        string transferHistory)
    {
        await using var updateCommand = cn.CreateCommand();

        updateCommand.CommandText = $"UPDATE driveMainIndex SET modified=@modified, hdrTransferHistory=@hdrTransferHistory " +
                                    $"WHERE identityId=@identityId AND driveid=@driveId AND fileId=@fileId;";

        var sparam1 = updateCommand.CreateParameter();
        var sparam2 = updateCommand.CreateParameter();
        var sparam3 = updateCommand.CreateParameter();
        var sparam4 = updateCommand.CreateParameter();
        var sparam5 = updateCommand.CreateParameter();

        sparam1.ParameterName = "@identityId";
        sparam2.ParameterName = "@driveId";
        sparam3.ParameterName = "@fileId";
        sparam4.ParameterName = "@hdrTransferHistory";
        sparam5.ParameterName = "@modified";

        updateCommand.Parameters.Add(sparam1);
        updateCommand.Parameters.Add(sparam2);
        updateCommand.Parameters.Add(sparam3);
        updateCommand.Parameters.Add(sparam4);
        updateCommand.Parameters.Add(sparam5);

        sparam1.Value = tenantId.ToByteArray();
        sparam2.Value = driveId.ToByteArray();
        sparam3.Value = fileId.ToByteArray();
        sparam4.Value = transferHistory;
        sparam5.Value = UnixTimeUtc.Now().milliseconds;

        return await updateCommand.ExecuteNonQueryAsync();
    }
}