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

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class TableDriveMainIndexMigrationV202507191211 : MigrationBase
    {
        public override Int64 MigrationVersion => 202507191211;
        public TableDriveMainIndexMigrationV202507191211(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task EnsureTableExistsAsync(IConnectionWrapper cn, bool dropExisting = false)
        {
            if (dropExisting)
                await MigrationBase.DeleteTableAsync(cn, "DriveMainIndexMigrationsV202507191211");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DriveMainIndexMigrationsV202507191211 IS '{ \"Version\": 202507191211 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE DriveMainIndexMigrationsV202507191211( -- { \"Version\": 202507191211 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"fileId BYTEA NOT NULL, "
                   +"globalTransitId BYTEA , "
                   +"fileState BIGINT NOT NULL, "
                   +"requiredSecurityGroup BIGINT NOT NULL, "
                   +"fileSystemType BIGINT NOT NULL, "
                   +"userDate BIGINT NOT NULL, "
                   +"fileType BIGINT NOT NULL, "
                   +"dataType BIGINT NOT NULL, "
                   +"archivalStatus BIGINT NOT NULL, "
                   +"historyStatus BIGINT NOT NULL, "
                   +"senderId TEXT , "
                   +"groupId BYTEA , "
                   +"uniqueId BYTEA , "
                   +"byteCount BIGINT NOT NULL, "
                   +"hdrEncryptedKeyHeader TEXT NOT NULL, "
                   +"hdrVersionTag BYTEA NOT NULL, "
                   +"hdrAppData TEXT NOT NULL, "
                   +"hdrLocalVersionTag BYTEA , "
                   +"hdrLocalAppData TEXT , "
                   +"hdrReactionSummary TEXT , "
                   +"hdrServerData TEXT NOT NULL, "
                   +"hdrTransferHistory TEXT , "
                   +"hdrFileMetaData TEXT NOT NULL, "
                   +"hdrTmpDriveAlias BYTEA NOT NULL, "
                   +"hdrTmpDriveType BYTEA NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,driveId,fileId)"
                   +", UNIQUE(identityId,driveId,uniqueId)"
                   +", UNIQUE(identityId,driveId,globalTransitId)"
                   +", UNIQUE(identityId,hdrVersionTag)"
                   +$"){wori};"
                   +"CREATE INDEX Idx0DriveMainIndexMigrationsV202507191211 ON DriveMainIndexMigrationsV202507191211(identityId,driveId,fileSystemType,requiredSecurityGroup,created,rowId);"
                   +"CREATE INDEX Idx1DriveMainIndexMigrationsV202507191211 ON DriveMainIndexMigrationsV202507191211(identityId,driveId,fileSystemType,requiredSecurityGroup,modified,rowId);"
                   +"CREATE INDEX Idx2DriveMainIndexMigrationsV202507191211 ON DriveMainIndexMigrationsV202507191211(identityId,driveId,fileSystemType,requiredSecurityGroup,userDate,rowId);"
                   ;
            await MigrationBase.CreateTableAsync(cn, createSql, commentSql);
        }

        public static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("driveId");
            sl.Add("fileId");
            sl.Add("globalTransitId");
            sl.Add("fileState");
            sl.Add("requiredSecurityGroup");
            sl.Add("fileSystemType");
            sl.Add("userDate");
            sl.Add("fileType");
            sl.Add("dataType");
            sl.Add("archivalStatus");
            sl.Add("historyStatus");
            sl.Add("senderId");
            sl.Add("groupId");
            sl.Add("uniqueId");
            sl.Add("byteCount");
            sl.Add("hdrEncryptedKeyHeader");
            sl.Add("hdrVersionTag");
            sl.Add("hdrAppData");
            sl.Add("hdrLocalVersionTag");
            sl.Add("hdrLocalAppData");
            sl.Add("hdrReactionSummary");
            sl.Add("hdrServerData");
            sl.Add("hdrTransferHistory");
            sl.Add("hdrFileMetaData");
            sl.Add("hdrTmpDriveAlias");
            sl.Add("hdrTmpDriveType");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO DriveMainIndexMigrationsV202507191211 (rowId,identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified) " +
               $"SELECT rowId,identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified "+
               $"FROM DriveMainIndex;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // DriveMainIndex is presumed to be the previous version
        // Will upgrade from the previous version to version 202507191211
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "DriveMainIndex", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await EnsureTableExistsAsync(cn, dropExisting: true);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "DriveMainIndex", "DriveMainIndexMigrationsV202507191211") == false)
                        throw new MigrationException("Mismatching row counts");
                    await RenameAsync(cn, "DriveMainIndex", $"DriveMainIndexMigrationsV{PreviousVersion}");
                    await RenameAsync(cn, "DriveMainIndexMigrationsV202507191211", "DriveMainIndex");
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
            // ASSERT MigrationVersion >= 0
            // ASSERT PreviousVersion >= 0
            // WHAT IF V202507... already exists?

            await CheckSqlTableVersion(cn, "DriveMainIndex", this.MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"DriveMainIndexMigrationsV{PreviousVersion}", "DriveMainIndex") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await RenameAsync(cn, "DriveMainIndex", "DriveMainIndexMigrationsV202507191211");
                    await RenameAsync(cn, $"DriveMainIndexMigrationsV{PreviousVersion}", "DriveMainIndex");
                    // ADD await CheckSqlTableVersion(cn, "DriveMainIndex", this.PreviousVersion);
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
