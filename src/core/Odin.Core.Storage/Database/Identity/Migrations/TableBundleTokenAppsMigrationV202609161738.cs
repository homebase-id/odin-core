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
    public class TableBundleTokenAppsMigrationV202609161738 : MigrationBase
    {
        public override Int64 MigrationVersion => 202609161738;
        public TableBundleTokenAppsMigrationV202609161738(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE BundleTokenAppsMigrationsV202609161738 IS '{ \"Version\": 202609161738 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS BundleTokenAppsMigrationsV202609161738( -- { \"Version\": 202609161738 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"tokenId BYTEA NOT NULL, "
                   +"appId BYTEA NOT NULL, "
                   +"encryptedKeyStoreKeyJson TEXT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,tokenId,appId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0BundleTokenAppsMigrationsV202609161738 ON BundleTokenAppsMigrationsV202609161738(identityId,appId);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "BundleTokenAppsMigrationsV202609161738", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("tokenId");
            sl.Add("appId");
            sl.Add("encryptedKeyStoreKeyJson");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "BundleTokenAppsMigrationsV202609161738", MigrationVersion);
            await CheckSqlTableVersion(cn, "BundleTokenApps", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO BundleTokenAppsMigrationsV202609161738 (rowId,identityId,tokenId,appId,encryptedKeyStoreKeyJson,created,modified) " +
               $"SELECT rowId,identityId,tokenId,appId,encryptedKeyStoreKeyJson,created,modified "+
               $"FROM BundleTokenApps;";
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
            await SqlHelper.RenameAsync(cn, "BundleTokenAppsMigrationsV202609161738", "BundleTokenApps");
            trn.Commit();
        }

        public override async Task DownAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "BundleTokenApps", MigrationVersion);
            await SqlHelper.DeleteTableAsync(cn, "BundleTokenApps");
        }

    }
}
