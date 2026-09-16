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
    public class TableBundleTokenAppsMigrationV0 : MigrationBase
    {
        public override Int64 MigrationVersion => 0;
        public TableBundleTokenAppsMigrationV0(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE BundleTokenAppsMigrationsV0 IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS BundleTokenAppsMigrationsV0( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"tokenId BYTEA NOT NULL, "
                   +"appId BYTEA NOT NULL, "
                   +"encryptedKeyStoreKeyJson TEXT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,tokenId,appId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0BundleTokenAppsMigrationsV0 ON BundleTokenAppsMigrationsV0(identityId,appId);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "BundleTokenAppsMigrationsV0", createSql, commentSql);
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
            await CheckSqlTableVersion(cn, "BundleTokenAppsMigrationsV0", MigrationVersion);
            await CheckSqlTableVersion(cn, "BundleTokenApps", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO BundleTokenAppsMigrationsV0 (rowId,identityId,tokenId,appId,encryptedKeyStoreKeyJson,created,modified) " +
               $"SELECT rowId,identityId,tokenId,appId,encryptedKeyStoreKeyJson,created,modified "+
               $"FROM BundleTokenApps;";
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
                    await SqlHelper.RenameAsync(cn, "BundleTokenAppsMigrationsV0", "BundleTokenApps");
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
            await CheckSqlTableVersion(cn, "BundleTokenApps", MigrationVersion);
            throw new  Exception("You cannot move down from version 0");
        }

    }
}
