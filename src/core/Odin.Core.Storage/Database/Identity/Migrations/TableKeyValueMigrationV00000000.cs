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
[assembly: InternalsVisibleTo("DatabaseConnectionTests")]

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class TableKeyValueMigrationV0 : MigrationBase
    {
        public override int MigrationVersion => 0;
        public TableKeyValueMigrationV0(MigrationListBase container) : base(container)
        {
        }

        public override async Task EnsureTableExistsAsync(IConnectionWrapper cn, bool dropExisting = false)
        {
            if (dropExisting)
                await MigrationBase.DeleteTableAsync(cn, "KeyValueMigrationsV0");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE KeyValueMigrationsV0 IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS KeyValueMigrationsV0( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"key BYTEA NOT NULL, "
                   +"data BYTEA  "
                   +", UNIQUE(identityId,key)"
                   +$"){wori};"
                   ;
            await MigrationBase.CreateTableAsync(cn, createSql, commentSql);
        }

        public static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("key");
            sl.Add("data");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO KeyValueMigrationsV0 (rowId,identityId,key,data) " +
               $"SELECT rowId,identityId,key,data "+
               $"FROM KeyValue;";
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
