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

namespace Odin.Core.Storage.Database.Identity.Migrations
{
    public class TableDriveTagIndexMigrationV0 : MigrationBase
    {
        public override Int64 MigrationVersion => 0;
        public TableDriveTagIndexMigrationV0(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DriveTagIndexMigrationsV0 IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS DriveTagIndexMigrationsV0( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"fileId BYTEA NOT NULL, "
                   +"tagId BYTEA NOT NULL "
                   +", UNIQUE(identityId,driveId,fileId,tagId)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "DriveTagIndexMigrationsV0", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("driveId");
            sl.Add("fileId");
            sl.Add("tagId");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "DriveTagIndexMigrationsV0", MigrationVersion);
            await CheckSqlTableVersion(cn, "DriveTagIndex", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO DriveTagIndexMigrationsV0 (rowId,identityId,driveId,fileId,tagId) " +
               $"SELECT rowId,identityId,driveId,fileId,tagId "+
               $"FROM DriveTagIndex;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // DriveMainIndex is presumed to be the previous version
        // Will upgrade from the previous version to version 0
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            // Create the initial table
            await CreateTableWithCommentAsync(cn);
        }

        public override async Task DownAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "DriveTagIndex", MigrationVersion);
            throw new  Exception("You cannot move down from version 0");
        }

    }
}
