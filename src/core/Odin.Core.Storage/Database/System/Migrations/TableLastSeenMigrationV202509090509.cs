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

namespace Odin.Core.Storage.Database.System.Migrations
{
    public class TableLastSeenMigrationV202509090509 : MigrationBase
    {
        public override Int64 MigrationVersion => 202509090509;
        public TableLastSeenMigrationV202509090509(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE LastSeenMigrationsV202509090509 IS '{ \"Version\": 202509090509 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS LastSeenMigrationsV202509090509( -- { \"Version\": 202509090509 }\n"
                   +rowid
                   +"odinId TEXT NOT NULL UNIQUE, "
                   +"timestamp BIGINT NOT NULL "
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "LastSeenMigrationsV202509090509", createSql, commentSql);
        }

        public override async Task UpAsync(IConnectionWrapper cn)
        {
            // Create the initial table
            await using var trn = await cn.BeginStackedTransactionAsync();
            await CreateTableWithCommentAsync(cn);
            await SqlHelper.RenameAsync(cn, "LastSeenMigrationsV202509090509", "LastSeen");
            trn.Commit();
        }

        public override async Task DownAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "LastSeen", MigrationVersion);
            await SqlHelper.DeleteTableAsync(cn, "LastSeen");
        }

    }
}
