using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Core.Storage;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Database.Identity.Connection;

#nullable disable

// THIS FILE WAS INITIALLY AUTO GENERATED

namespace Odin.Core.Storage.Database.Identity.Migrations
{
    public class TableDriveTransferHistoryMigrationV202604050941 : MigrationBase
    {
        public override Int64 MigrationVersion => 202604050941;
        public TableDriveTransferHistoryMigrationV202604050941(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DriveTransferHistoryMigrationsV202604050941 IS '{ \"Version\": 202604050941 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS DriveTransferHistoryMigrationsV202604050941( -- { \"Version\": 202604050941 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"fileId BYTEA NOT NULL, "
                   +"remoteIdentityId TEXT NOT NULL, "
                   +"latestTransferStatus BIGINT NOT NULL, "
                   +"isInOutbox BOOLEAN NOT NULL, "
                   +"latestSuccessfullyDeliveredVersionTag BYTEA , "
                   +"isReadByRecipient BIGINT NOT NULL "
                   +", UNIQUE(identityId,driveId,fileId,remoteIdentityId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0DriveTransferHistoryMigrationsV202604050941 ON DriveTransferHistoryMigrationsV202604050941(identityId,driveId,fileId);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "DriveTransferHistoryMigrationsV202604050941", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("driveId");
            sl.Add("fileId");
            sl.Add("remoteIdentityId");
            sl.Add("latestTransferStatus");
            sl.Add("isInOutbox");
            sl.Add("latestSuccessfullyDeliveredVersionTag");
            sl.Add("isReadByRecipient");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "DriveTransferHistoryMigrationsV202604050941", MigrationVersion);
            await CheckSqlTableVersion(cn, "DriveTransferHistory", PreviousVersion);
            var nowMs = UnixTimeUtc.Now().milliseconds;
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO DriveTransferHistoryMigrationsV202604050941 (rowId,identityId,driveId,fileId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient) " +
               $"SELECT rowId,identityId,driveId,fileId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag," +
               $"CASE WHEN isReadByRecipient = 1 THEN {nowMs} ELSE 0 END "+
               $"FROM DriveTransferHistory;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202604050941
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "DriveTransferHistory", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "DriveTransferHistoryMigrationsV202604050941", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "DriveTransferHistory", "DriveTransferHistoryMigrationsV202604050941") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "DriveTransferHistory", $"DriveTransferHistoryMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "DriveTransferHistoryMigrationsV202604050941", "DriveTransferHistory");
                    await CheckSqlTableVersion(cn, "DriveTransferHistory", MigrationVersion);
                    trn.Commit();
                }
            }
            catch
            {
                throw;
            }
        }

        public override async Task DownAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "DriveTransferHistory", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"DriveTransferHistoryMigrationsV{PreviousVersion}", "DriveTransferHistory") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "DriveTransferHistory", "DriveTransferHistoryMigrationsV202604050941");
                    await SqlHelper.RenameAsync(cn, $"DriveTransferHistoryMigrationsV{PreviousVersion}", "DriveTransferHistory");
                    await CheckSqlTableVersion(cn, "DriveTransferHistory", PreviousVersion);
                    trn.Commit();
                }
            }
            catch
            {
                throw;
            }
        }

    }
}
