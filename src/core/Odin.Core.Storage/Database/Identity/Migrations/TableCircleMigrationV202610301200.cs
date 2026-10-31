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
    public class TableCircleMigrationV202610301200 : MigrationBase
    {
        public override Int64 MigrationVersion => 202610301200;
        public TableCircleMigrationV202610301200(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE CircleMigrationsV202610301200 IS '{ \"Version\": 202610301200 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS CircleMigrationsV202610301200( -- { \"Version\": 202610301200 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"circleId BYTEA NOT NULL, "
                   +"circleName TEXT NOT NULL, "
                   +"data BYTEA , "
                   +"AppId BYTEA , "
                   +"CircleSlug TEXT NOT NULL, "
                   +"GrantOn BIGINT NOT NULL DEFAULT 0, "
                   +"Designation BIGINT NOT NULL DEFAULT 1, "
                   +"Emoji TEXT , "
                   +"appKeyLockedShareKey BYTEA  "
                   +", UNIQUE(identityId,circleId)"
                   +", UNIQUE(identityId,AppId,CircleSlug)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0CircleMigrationsV202610301200 ON CircleMigrationsV202610301200(identityId,AppId);"
                   +"CREATE INDEX IF NOT EXISTS Idx1CircleMigrationsV202610301200 ON CircleMigrationsV202610301200(identityId,GrantOn);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "CircleMigrationsV202610301200", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("circleId");
            sl.Add("circleName");
            sl.Add("data");
            sl.Add("AppId");
            sl.Add("CircleSlug");
            sl.Add("GrantOn");
            sl.Add("Designation");
            sl.Add("Emoji");
            sl.Add("appKeyLockedShareKey");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "CircleMigrationsV202610301200", MigrationVersion);
            await CheckSqlTableVersion(cn, "Circle", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                // appKeyLockedShareKey is new in this version and is omitted from both lists: every existing circle lands
                // as NULL until the owning app first shares with it (docs/selective-share-plan.md).
                copyCommand.CommandText = "INSERT INTO CircleMigrationsV202610301200 (rowId,identityId,circleId,circleName,data,AppId,CircleSlug,GrantOn,Designation,Emoji) " +
               $"SELECT rowId,identityId,circleId,circleName,data,AppId,CircleSlug,GrantOn,Designation,Emoji "+
               $"FROM Circle;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202610301200
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "Circle", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "CircleMigrationsV202610301200", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "Circle", "CircleMigrationsV202610301200") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "Circle", $"CircleMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "CircleMigrationsV202610301200", "Circle");
                    await CheckSqlTableVersion(cn, "Circle", MigrationVersion);
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
            await CheckSqlTableVersion(cn, "Circle", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"CircleMigrationsV{PreviousVersion}", "Circle") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "Circle", "CircleMigrationsV202610301200");
                    await SqlHelper.RenameAsync(cn, $"CircleMigrationsV{PreviousVersion}", "Circle");
                    await CheckSqlTableVersion(cn, "Circle", PreviousVersion);
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
