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
    public class TableMySubscribersMigrationV0 : MigrationBase
    {
        public override Int64 MigrationVersion => 0;
        public TableMySubscribersMigrationV0(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE MySubscribersMigrationsV0 IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS MySubscribersMigrationsV0( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"subscriberOdinId TEXT NOT NULL, "
                   +"sourceDriveId BYTEA , "
                   +"sourceDriveTypeId BYTEA , "
                   +"targetDriveId BYTEA , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,subscriberOdinId,sourceDriveId,sourceDriveTypeId,targetDriveId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0MySubscribersMigrationsV0 ON MySubscribersMigrationsV0(identityId,subscriberOdinId);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "MySubscribersMigrationsV0", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("subscriberOdinId");
            sl.Add("sourceDriveId");
            sl.Add("sourceDriveTypeId");
            sl.Add("targetDriveId");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "MySubscribersMigrationsV0", MigrationVersion);
            await CheckSqlTableVersion(cn, "MySubscribers", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO MySubscribersMigrationsV0 (rowId,identityId,subscriberOdinId,sourceDriveId,sourceDriveTypeId,targetDriveId,created,modified) " +
               $"SELECT rowId,identityId,subscriberOdinId,sourceDriveId,sourceDriveTypeId,targetDriveId,created,modified "+
               $"FROM MySubscribers;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 0
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    // Create the initial table
                    await CreateTableWithCommentAsync(cn);
                    await SqlHelper.RenameAsync(cn, "MySubscribersMigrationsV0", "MySubscribers");
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
            await CheckSqlTableVersion(cn, "MySubscribers", MigrationVersion);
            throw new  Exception("You cannot move down from version 0");
        }

    }
}
