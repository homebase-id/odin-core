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
    public class TableDriveMainIndexMigrationV202611191400 : MigrationBase
    {
        public override Int64 MigrationVersion => 202611191400;
        public TableDriveMainIndexMigrationV202611191400(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DriveMainIndexMigrationsV202611191400 IS '{ \"Version\": 202611191400 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS DriveMainIndexMigrationsV202611191400( -- { \"Version\": 202611191400 }\n"
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
                   +"versionTagModified BIGINT NOT NULL DEFAULT 0, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", CONSTRAINT DriveMainIndexMigrationsV202611191400_fileId UNIQUE(identityId,driveId,fileId)"
                   +", CONSTRAINT DriveMainIndexMigrationsV202611191400_uniqueId UNIQUE(identityId,driveId,uniqueId)"
                   +", CONSTRAINT DriveMainIndexMigrationsV202611191400_globalTransitId UNIQUE(identityId,driveId,globalTransitId)"
                   +", CONSTRAINT DriveMainIndexMigrationsV202611191400_hdrVersionTag UNIQUE(identityId,hdrVersionTag)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0DriveMainIndexMigrationsV202611191400 ON DriveMainIndexMigrationsV202611191400(identityId,driveId,fileSystemType,requiredSecurityGroup,created,rowId);"
                   +"CREATE INDEX IF NOT EXISTS Idx1DriveMainIndexMigrationsV202611191400 ON DriveMainIndexMigrationsV202611191400(identityId,driveId,fileSystemType,requiredSecurityGroup,modified,rowId);"
                   +"CREATE INDEX IF NOT EXISTS Idx2DriveMainIndexMigrationsV202611191400 ON DriveMainIndexMigrationsV202611191400(identityId,driveId,fileSystemType,requiredSecurityGroup,userDate,rowId);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "DriveMainIndexMigrationsV202611191400", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
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
            sl.Add("versionTagModified");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "DriveMainIndexMigrationsV202611191400", MigrationVersion);
            await CheckSqlTableVersion(cn, "DriveMainIndex", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                // Same columns as the previous version. This migration only names the UNIQUE constraints
                // <table>_<column>: Postgres's own names are cut to 63 characters, which loses the column, and
                // OdinDatabaseException.IsUniqueConstraintViolationOn reads the column from the name (#2085).
                copyCommand.CommandText = "INSERT INTO DriveMainIndexMigrationsV202611191400 (rowId,identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,versionTagModified,created,modified) " +
               $"SELECT rowId,identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,versionTagModified,created,modified "+
               $"FROM DriveMainIndex;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202611191400
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "DriveMainIndex", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "DriveMainIndexMigrationsV202611191400", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "DriveMainIndex", "DriveMainIndexMigrationsV202611191400") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "DriveMainIndex", $"DriveMainIndexMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "DriveMainIndexMigrationsV202611191400", "DriveMainIndex");
                    await CheckSqlTableVersion(cn, "DriveMainIndex", MigrationVersion);
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
            await CheckSqlTableVersion(cn, "DriveMainIndex", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"DriveMainIndexMigrationsV{PreviousVersion}", "DriveMainIndex") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "DriveMainIndex", "DriveMainIndexMigrationsV202611191400");
                    await SqlHelper.RenameAsync(cn, $"DriveMainIndexMigrationsV{PreviousVersion}", "DriveMainIndex");
                    await CheckSqlTableVersion(cn, "DriveMainIndex", PreviousVersion);
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
