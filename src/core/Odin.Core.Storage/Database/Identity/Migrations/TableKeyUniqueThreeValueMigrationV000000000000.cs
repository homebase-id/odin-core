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
    public class TableKeyUniqueThreeValueMigrationV0 : MigrationBase
    {
        public override Int64 MigrationVersion => 0;
        public TableKeyUniqueThreeValueMigrationV0(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE KeyUniqueThreeValueMigrationsV0 IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS KeyUniqueThreeValueMigrationsV0( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"key1 BYTEA NOT NULL, "
                   +"key2 BYTEA NOT NULL, "
                   +"key3 BYTEA NOT NULL, "
                   +"data BYTEA  "
                   +", UNIQUE(identityId,key1)"
                   +", UNIQUE(identityId,key2,key3)"
                   +$"){wori};"
                   +"CREATE INDEX Idx0KeyUniqueThreeValueMigrationsV0 ON KeyUniqueThreeValueMigrationsV0(identityId,key2);"
                   +"CREATE INDEX Idx1KeyUniqueThreeValueMigrationsV0 ON KeyUniqueThreeValueMigrationsV0(key3);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "KeyUniqueThreeValueMigrationsV0", createSql, commentSql);
        }

        public static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("key1");
            sl.Add("key2");
            sl.Add("key3");
            sl.Add("data");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "KeyUniqueThreeValueMigrationsV0", MigrationVersion);
            await CheckSqlTableVersion(cn, "KeyUniqueThreeValue", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO KeyUniqueThreeValueMigrationsV0 (rowId,identityId,key1,key2,key3,data) " +
               $"SELECT rowId,identityId,key1,key2,key3,data "+
               $"FROM KeyUniqueThreeValue;";
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
