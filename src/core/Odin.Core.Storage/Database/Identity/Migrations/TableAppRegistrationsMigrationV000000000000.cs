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
    public class TableAppRegistrationsMigrationV0 : MigrationBase
    {
        public override Int64 MigrationVersion => 0;
        public TableAppRegistrationsMigrationV0(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE AppRegistrationsMigrationsV0 IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS AppRegistrationsMigrationsV0( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"AppId BYTEA NOT NULL, "
                   +"AppSlug TEXT NOT NULL, "
                   +"Name TEXT NOT NULL, "
                   +"CorsHostName TEXT , "
                   +"grantJson TEXT NOT NULL, "
                   +"detailsJson TEXT , "
                   +"AutoConnectDefaults BOOLEAN NOT NULL DEFAULT FALSE, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,AppId)"
                   +", UNIQUE(identityId,AppSlug)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "AppRegistrationsMigrationsV0", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("AppId");
            sl.Add("AppSlug");
            sl.Add("Name");
            sl.Add("CorsHostName");
            sl.Add("grantJson");
            sl.Add("detailsJson");
            sl.Add("AutoConnectDefaults");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "AppRegistrationsMigrationsV0", MigrationVersion);
            await CheckSqlTableVersion(cn, "AppRegistrations", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO AppRegistrationsMigrationsV0 (rowId,identityId,AppId,AppSlug,Name,CorsHostName,grantJson,detailsJson,AutoConnectDefaults,created,modified) " +
               $"SELECT rowId,identityId,AppId,AppSlug,Name,CorsHostName,grantJson,detailsJson,AutoConnectDefaults,created,modified "+
               $"FROM AppRegistrations;";
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
                    await SqlHelper.RenameAsync(cn, "AppRegistrationsMigrationsV0", "AppRegistrations");
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
            await CheckSqlTableVersion(cn, "AppRegistrations", MigrationVersion);
            throw new  Exception("You cannot move down from version 0");
        }

    }
}
