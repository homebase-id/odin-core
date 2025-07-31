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

[assembly: InternalsVisibleTo("DatabaseCommitTest")]

namespace Odin.Core.Storage.Database.Identity.Migrations
{
    public class TableFollowsMeMigrationV0 : MigrationBase
    {
        public override Int64 MigrationVersion => 0;
        public TableFollowsMeMigrationV0(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE FollowsMeMigrationsV0 IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS FollowsMeMigrationsV0( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"identity TEXT NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,identity,driveId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0FollowsMeMigrationsV0 ON FollowsMeMigrationsV0(identityId,identity);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "FollowsMeMigrationsV0", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("identity");
            sl.Add("driveId");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "FollowsMeMigrationsV0", MigrationVersion);
            await CheckSqlTableVersion(cn, "FollowsMe", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO FollowsMeMigrationsV0 (rowId,identityId,identity,driveId,created,modified) " +
               $"SELECT rowId,identityId,identity,driveId,created,modified "+
               $"FROM FollowsMe;";
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
            await CheckSqlTableVersion(cn, "FollowsMe", MigrationVersion);
            throw new  Exception("You cannot move down from version 0");
        }

    }
}
