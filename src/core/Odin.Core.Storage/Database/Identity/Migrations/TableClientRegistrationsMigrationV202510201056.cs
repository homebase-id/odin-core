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
    public class TableClientRegistrationsMigrationV202510201056 : MigrationBase
    {
        public override Int64 MigrationVersion => 202510201056;
        public TableClientRegistrationsMigrationV202510201056(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE ClientRegistrationsMigrationsV202510201056 IS '{ \"Version\": 202510201056 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS ClientRegistrationsMigrationsV202510201056( -- { \"Version\": 202510201056 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"catId BYTEA NOT NULL UNIQUE, "
                   +"issuedToId TEXT NOT NULL, "
                   +"ttl BIGINT NOT NULL, "
                   +"expiresAt BIGINT NOT NULL, "
                   +"categoryId BYTEA NOT NULL UNIQUE, "
                   +"catType BIGINT NOT NULL, "
                   +"value TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,catId)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "ClientRegistrationsMigrationsV202510201056", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("catId");
            sl.Add("issuedToId");
            sl.Add("ttl");
            sl.Add("expiresAt");
            sl.Add("categoryId");
            sl.Add("catType");
            sl.Add("value");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "ClientRegistrationsMigrationsV202510201056", MigrationVersion);
            await CheckSqlTableVersion(cn, "ClientRegistrations", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO ClientRegistrationsMigrationsV202510201056 (rowId,identityId,catId,issuedToId,ttl,expiresAt,categoryId,catType,value,created,modified) " +
               $"SELECT rowId,identityId,catId,issuedToId,ttl,expiresAt,categoryId,catType,value,created,modified "+
               $"FROM ClientRegistrations;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202510201056
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "ClientRegistrations", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "ClientRegistrationsMigrationsV202510201056", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "ClientRegistrations", "ClientRegistrationsMigrationsV202510201056") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "ClientRegistrations", $"ClientRegistrationsMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "ClientRegistrationsMigrationsV202510201056", "ClientRegistrations");
                    await CheckSqlTableVersion(cn, "ClientRegistrations", MigrationVersion);
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
            await CheckSqlTableVersion(cn, "ClientRegistrations", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"ClientRegistrationsMigrationsV{PreviousVersion}", "ClientRegistrations") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "ClientRegistrations", "ClientRegistrationsMigrationsV202510201056");
                    await SqlHelper.RenameAsync(cn, $"ClientRegistrationsMigrationsV{PreviousVersion}", "ClientRegistrations");
                    await CheckSqlTableVersion(cn, "ClientRegistrations", PreviousVersion);
                    trn.Commit();
                }
            }
            catch
            {
                throw;
            }
        }

    }
}
