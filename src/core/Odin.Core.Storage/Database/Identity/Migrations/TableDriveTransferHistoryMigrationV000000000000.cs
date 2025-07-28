using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.SQLite;

// THIS FILE WAS INITIALLY AUTO GENERATED

namespace Odin.Core.Storage.Database.Identity
{
    public class TableDriveTransferHistoryMigrationV0 : MigrationBase
    {
        public override Int64 MigrationVersion => 0;
        public TableDriveTransferHistoryMigrationV0(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DriveTransferHistoryMigrationsV0 IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS DriveTransferHistoryMigrationsV0( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"fileId BYTEA NOT NULL, "
                   +"remoteIdentityId TEXT NOT NULL, "
                   +"latestTransferStatus BIGINT NOT NULL, "
                   +"isInOutbox BOOLEAN NOT NULL, "
                   +"latestSuccessfullyDeliveredVersionTag BYTEA , "
                   +"isReadByRecipient BOOLEAN NOT NULL "
                   +", UNIQUE(identityId,driveId,fileId,remoteIdentityId)"
                   +$"){wori};"
                   +"CREATE INDEX Idx0DriveTransferHistoryMigrationsV0 ON DriveTransferHistoryMigrationsV0(identityId,driveId,fileId);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "DriveTransferHistoryMigrationsV0", createSql, commentSql);
        }

        public static List<string> GetColumnNames()
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
            await CheckSqlTableVersion(cn, "DriveTransferHistoryMigrationsV0", MigrationVersion);
            await CheckSqlTableVersion(cn, "DriveTransferHistory", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO DriveTransferHistoryMigrationsV0 (rowId,identityId,driveId,fileId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient) " +
               $"SELECT rowId,identityId,driveId,fileId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient "+
               $"FROM DriveTransferHistory;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // DriveMainIndex is presumed to be the previous version
        // Will upgrade from the previous version to version 0
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await Task.Delay(0);
            throw new  Exception("You cannot move up from version 0");
        }

        public override async Task DownAsync(IConnectionWrapper cn)
        {
            await Task.Delay(0);
            throw new  Exception("You cannot move down from version 0");
        }

    }
}
