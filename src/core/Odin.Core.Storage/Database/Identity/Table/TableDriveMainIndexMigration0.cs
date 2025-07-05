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

// THIS FILE IS INITIALLY AUTO GENERATED - DO NOT EDIT UNTIL YOU DISABLED ITS GENERATION

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class TableDriveMainIndexMigration0 : Migration
    {
        public override int MigrationVersion => 0;
        public override Migration DownMigration => null;
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;
        public TableDriveMainIndexMigration0(ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
           _scopedConnectionFactory = scopedConnectionFactory;
        }
        public virtual async Task<int> EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS DriveMainIndexMigration0;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS DriveMainIndexMigration0("
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
                   +"CREATE INDEX IF NOT EXISTS Idx0DriveMainIndexMigration0 ON DriveMainIndexMigration0(identityId,driveId,fileSystemType,requiredSecurityGroup,created,rowId);"
                   +"CREATE INDEX IF NOT EXISTS Idx1DriveMainIndexMigration0 ON DriveMainIndexMigration0(identityId,driveId,fileSystemType,requiredSecurityGroup,modified,rowId);"
                   +"CREATE INDEX IF NOT EXISTS Idx2DriveMainIndexMigration0 ON DriveMainIndexMigration0(identityId,driveId,fileSystemType,requiredSecurityGroup,userDate,rowId);"
                   ;
            return await cmd.ExecuteNonQueryAsync();
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
                copyCommand.CommandText = "INSERT INTO DriveMainIndexMigration0 (rowId,identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified) " +
               $"SELECT rowId,identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified "+
               $"FROM DriveMainIndex;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        public async Task UpAsync(IConnectionWrapper cn)
        {
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await EnsureTableExistsAsync(dropExisting: true) != 1)
                        throw new MigrationException("Unable to create the new table");
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await RenameAsync(cn, "DriveMainIndex", $"DriveMainIndexMigration+{PreviousVersion()}") != 1)
                        throw new MigrationException("Unable to do the first rename");
                   if (await RenameAsync(cn, "DriveMainIndexMigration0", "DriveMainIndex") != 1)
                        throw new MigrationException("Unable to do the second rename");
                    trn.Commit();
                }
            }
            catch
            {
                throw;
            }
        }

        public async Task DownAsync(IConnectionWrapper cn)
        {
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                   if (await RenameAsync(cn, "DriveMainIndex", "DriveMainIndexMigration0") < 1)
                        throw new MigrationException("Unable to do the first rename");
                   if (await RenameAsync(cn, $"DriveMainIndexMigration+{PreviousVersion().ToString()}", "DriveMainIndex") < 1)
                        throw new MigrationException("Unable to do the second rename");
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
