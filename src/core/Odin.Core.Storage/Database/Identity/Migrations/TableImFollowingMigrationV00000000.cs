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
    public class TableImFollowingMigrationV0 : MigrationBase
    {
        public override int MigrationVersion => 0;
        public TableImFollowingMigrationV0(MigrationListBase container) : base(container)
        {
        }

        public override async Task EnsureTableExistsAsync(IConnectionWrapper cn, bool dropExisting = false)
        {
            if (dropExisting)
                await MigrationBase.DeleteTableAsync(cn, "ImFollowingMigrationsV0");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE ImFollowingMigrationsV0 IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS ImFollowingMigrationsV0( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"identity TEXT NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,identity,driveId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0ImFollowingMigrationsV0 ON ImFollowingMigrationsV0(identityId,identity);"
                   ;
            await MigrationBase.CreateTableAsync(cn, createSql, commentSql);
        }

        public static List<string> GetColumnNames()
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
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO ImFollowingMigrationsV0 (rowId,identityId,identity,driveId,created,modified) " +
               $"SELECT rowId,identityId,identity,driveId,created,modified "+
               $"FROM ImFollowing;";
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
