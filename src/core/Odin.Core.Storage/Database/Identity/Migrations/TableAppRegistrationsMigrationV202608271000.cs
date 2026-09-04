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
    public class TableAppRegistrationsMigrationV202608271000 : MigrationBase
    {
        public override Int64 MigrationVersion => 202608271000;
        public TableAppRegistrationsMigrationV202608271000(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE AppRegistrationsMigrationsV202608271000 IS '{ \"Version\": 202608271000 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS AppRegistrationsMigrationsV202608271000( -- { \"Version\": 202608271000 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"AppId BYTEA NOT NULL, "
                   +"AppSlug TEXT NOT NULL, "
                   +"Name TEXT NOT NULL, "
                   +"CorsHostName TEXT , "
                   +"grantJson TEXT NOT NULL, "
                   +"detailsJson TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,AppId)"
                   +", UNIQUE(identityId,AppSlug)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "AppRegistrationsMigrationsV202608271000", createSql, commentSql);
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
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "AppRegistrationsMigrationsV202608271000", MigrationVersion);
            await CheckSqlTableVersion(cn, "AppRegistrations", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO AppRegistrationsMigrationsV202608271000 (rowId,identityId,AppId,AppSlug,Name,CorsHostName,grantJson,detailsJson,created,modified) " +
               $"SELECT rowId,identityId,AppId,AppSlug,Name,CorsHostName,grantJson,detailsJson,created,modified "+
               $"FROM AppRegistrations;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202608271000
        // HAND-EDITED, and the generator needs the same fix: AppRegistrations shipped with a version-0
        // migration only. The migrator keeps one version per database and going up runs only the groups
        // above it, so version 0 was unreachable on every database already past it -- the table was never
        // created there. It exists, at version 0, only on databases created since it was added. Both
        // populations have to arrive at this version, so Up branches on which one it is.
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            if (!await cn.TableExistsAsync("AppRegistrations"))
            {
                // Never created here. There is nothing to copy and nothing to rename out of the way.
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "AppRegistrationsMigrationsV202608271000", MigrationVersion);
                    await SqlHelper.RenameAsync(cn, "AppRegistrationsMigrationsV202608271000", "AppRegistrations");
                    await CheckSqlTableVersion(cn, "AppRegistrations", MigrationVersion);
                    trn.Commit();
                }

                return;
            }

            // Present at the previous version. Rebuilding is the only way to restamp it: on SQLite the
            // version marker lives inside the stored CREATE TABLE text.
            await CheckSqlTableVersion(cn, "AppRegistrations", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "AppRegistrationsMigrationsV202608271000", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "AppRegistrations", "AppRegistrationsMigrationsV202608271000") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "AppRegistrations", $"AppRegistrationsMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "AppRegistrationsMigrationsV202608271000", "AppRegistrations");
                    await CheckSqlTableVersion(cn, "AppRegistrations", MigrationVersion);
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

            if (!await cn.TableExistsAsync($"AppRegistrationsMigrationsV{PreviousVersion}"))
            {
                // Up created the table rather than rebuilding one, so there is no previous version to
                // restore to. Undoing means the table is gone again.
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await SqlHelper.DeleteTableAsync(cn, "AppRegistrations");
                    trn.Commit();
                }

                return;
            }

            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"AppRegistrationsMigrationsV{PreviousVersion}", "AppRegistrations") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "AppRegistrations", "AppRegistrationsMigrationsV202608271000");
                    await SqlHelper.RenameAsync(cn, $"AppRegistrationsMigrationsV{PreviousVersion}", "AppRegistrations");
                    await CheckSqlTableVersion(cn, "AppRegistrations", PreviousVersion);
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
