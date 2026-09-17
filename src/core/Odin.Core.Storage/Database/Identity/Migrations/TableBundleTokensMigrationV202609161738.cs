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
    public class TableBundleTokensMigrationV202609161738 : MigrationBase
    {
        public override Int64 MigrationVersion => 202609161738;
        public TableBundleTokensMigrationV202609161738(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE BundleTokensMigrationsV202609161738 IS '{ \"Version\": 202609161738 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS BundleTokensMigrationsV202609161738( -- { \"Version\": 202609161738 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"tokenId BYTEA NOT NULL, "
                   +"primaryAppId BYTEA NOT NULL, "
                   +"friendlyName TEXT NOT NULL, "
                   +"accessRegistrationJson TEXT NOT NULL, "
                   +"expiresAt BIGINT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,tokenId)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "BundleTokensMigrationsV202609161738", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("tokenId");
            sl.Add("primaryAppId");
            sl.Add("friendlyName");
            sl.Add("accessRegistrationJson");
            sl.Add("expiresAt");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "BundleTokensMigrationsV202609161738", MigrationVersion);
            await CheckSqlTableVersion(cn, "BundleTokens", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO BundleTokensMigrationsV202609161738 (rowId,identityId,tokenId,primaryAppId,friendlyName,accessRegistrationJson,expiresAt,created,modified) " +
               $"SELECT rowId,identityId,tokenId,primaryAppId,friendlyName,accessRegistrationJson,expiresAt,created,modified "+
               $"FROM BundleTokens;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // This is the table's first migration (previousVersion -1): there is nothing
        // to copy or rename away from, so it just creates the table - the same
        // hand-adjusted shape as TableDkimKeysMigrationV202608201100.
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            // Create the initial table
            await using var trn = await cn.BeginStackedTransactionAsync();
            await CreateTableWithCommentAsync(cn);
            await SqlHelper.RenameAsync(cn, "BundleTokensMigrationsV202609161738", "BundleTokens");
            trn.Commit();
        }

        public override async Task DownAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "BundleTokens", MigrationVersion);
            await SqlHelper.DeleteTableAsync(cn, "BundleTokens");
        }

    }
}
