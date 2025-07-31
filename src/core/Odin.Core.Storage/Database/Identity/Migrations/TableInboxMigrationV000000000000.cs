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
    public class TableInboxMigrationV0 : MigrationBase
    {
        public override Int64 MigrationVersion => 0;
        public TableInboxMigrationV0(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE InboxMigrationsV0 IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS InboxMigrationsV0( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"fileId BYTEA NOT NULL UNIQUE, "
                   +"boxId BYTEA NOT NULL, "
                   +"priority BIGINT NOT NULL, "
                   +"timeStamp BIGINT NOT NULL, "
                   +"value BYTEA , "
                   +"popStamp BYTEA , "
                   +"correlationId TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,fileId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0InboxMigrationsV0 ON InboxMigrationsV0(identityId,timeStamp);"
                   +"CREATE INDEX IF NOT EXISTS Idx1InboxMigrationsV0 ON InboxMigrationsV0(identityId,boxId);"
                   +"CREATE INDEX IF NOT EXISTS Idx2InboxMigrationsV0 ON InboxMigrationsV0(identityId,popStamp);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "InboxMigrationsV0", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("fileId");
            sl.Add("boxId");
            sl.Add("priority");
            sl.Add("timeStamp");
            sl.Add("value");
            sl.Add("popStamp");
            sl.Add("correlationId");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "InboxMigrationsV0", MigrationVersion);
            await CheckSqlTableVersion(cn, "Inbox", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO InboxMigrationsV0 (rowId,identityId,fileId,boxId,priority,timeStamp,value,popStamp,correlationId,created,modified) " +
               $"SELECT rowId,identityId,fileId,boxId,priority,timeStamp,value,popStamp,correlationId,created,modified "+
               $"FROM Inbox;";
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
            await CheckSqlTableVersion(cn, "Inbox", MigrationVersion);
            throw new  Exception("You cannot move down from version 0");
        }

    }
}
