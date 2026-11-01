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
    public class TableDriveAclIndexMigrationV202610301200 : MigrationBase
    {
        public override Int64 MigrationVersion => 202610301200;
        public TableDriveAclIndexMigrationV202610301200(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DriveAclIndexMigrationsV202610301200 IS '{ \"Version\": 202610301200 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS DriveAclIndexMigrationsV202610301200( -- { \"Version\": 202610301200 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"fileId BYTEA NOT NULL, "
                   +"aclMemberId BYTEA NOT NULL, "
                   +"expiresAt BIGINT , "
                   +"permissions BIGINT NOT NULL DEFAULT 1, "
                   +"lockedKeyHeader BYTEA , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,driveId,fileId,aclMemberId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0DriveAclIndexMigrationsV202610301200 ON DriveAclIndexMigrationsV202610301200(identityId,driveId,aclMemberId,modified,rowId);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "DriveAclIndexMigrationsV202610301200", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("driveId");
            sl.Add("fileId");
            sl.Add("aclMemberId");
            sl.Add("expiresAt");
            sl.Add("permissions");
            sl.Add("lockedKeyHeader");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "DriveAclIndexMigrationsV202610301200", MigrationVersion);
            await CheckSqlTableVersion(cn, "DriveAclIndex", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                // expiresAt, permissions, lockedKeyHeader, created and modified are new in this version.
                // Every existing row lands as NULL, which never expires, as 1, read, which is what every row
                // meant before, and with no locked key header: none of them is a share yet. created and
                // modified are NOT NULL with no default, so they get the migration's time
                // (docs/selective-share-plan.md).
                string sqlNowStr = copyCommand.SqlNow();
                copyCommand.CommandText = "INSERT INTO DriveAclIndexMigrationsV202610301200 (rowId,identityId,driveId,fileId,aclMemberId,created,modified) " +
               $"SELECT rowId,identityId,driveId,fileId,aclMemberId,{sqlNowStr},{sqlNowStr} "+
               $"FROM DriveAclIndex;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202610301200
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "DriveAclIndex", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "DriveAclIndexMigrationsV202610301200", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "DriveAclIndex", "DriveAclIndexMigrationsV202610301200") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "DriveAclIndex", $"DriveAclIndexMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "DriveAclIndexMigrationsV202610301200", "DriveAclIndex");
                    await CheckSqlTableVersion(cn, "DriveAclIndex", MigrationVersion);
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
            await CheckSqlTableVersion(cn, "DriveAclIndex", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"DriveAclIndexMigrationsV{PreviousVersion}", "DriveAclIndex") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "DriveAclIndex", "DriveAclIndexMigrationsV202610301200");
                    await SqlHelper.RenameAsync(cn, $"DriveAclIndexMigrationsV{PreviousVersion}", "DriveAclIndex");
                    await CheckSqlTableVersion(cn, "DriveAclIndex", PreviousVersion);
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
