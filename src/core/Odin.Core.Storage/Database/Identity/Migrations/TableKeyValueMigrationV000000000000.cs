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

namespace Odin.Core.Storage.Database.Identity.Migrations
{
    public class TableKeyValueMigrationV0 : MigrationBase
    {
        public override Int64 MigrationVersion => 0;
        public TableKeyValueMigrationV0(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
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
            await SqlHelper.CreateTableWithCommentAsync(cn, "KeyValueMigrationsV0", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
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
            await CheckSqlTableVersion(cn, "KeyValueMigrationsV0", MigrationVersion);
            await CheckSqlTableVersion(cn, "KeyValue", PreviousVersion);
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
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    // Create the initial table
                    await CreateTableWithCommentAsync(cn);
                    await SqlHelper.RenameAsync(cn, "KeyValueMigrationsV0", "KeyValue");
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
            await CheckSqlTableVersion(cn, "KeyValue", MigrationVersion);
            throw new  Exception("You cannot move down from version 0");
        }

    }
}
