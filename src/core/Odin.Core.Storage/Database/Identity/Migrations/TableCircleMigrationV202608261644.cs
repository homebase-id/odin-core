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
    public class TableCircleMigrationV202608261644 : MigrationBase
    {
        public override Int64 MigrationVersion => 202608261644;
        public TableCircleMigrationV202608261644(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE CircleMigrationsV202608261644 IS '{ \"Version\": 202608261644 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS CircleMigrationsV202608261644( -- { \"Version\": 202608261644 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"circleId BYTEA NOT NULL, "
                   +"circleName TEXT NOT NULL, "
                   +"data BYTEA , "
                   +"AppId BYTEA , "
                   +"GrantOn BIGINT NOT NULL DEFAULT 0, "
                   +"Designation BIGINT NOT NULL DEFAULT 1, "
                   +"Emoji TEXT  "
                   +", UNIQUE(identityId,circleId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0CircleMigrationsV202608261644 ON CircleMigrationsV202608261644(identityId,AppId);"
                   +"CREATE INDEX IF NOT EXISTS Idx1CircleMigrationsV202608261644 ON CircleMigrationsV202608261644(identityId,GrantOn);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "CircleMigrationsV202608261644", createSql, commentSql);
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
            sl.Add("GrantOn");
            sl.Add("Designation");
            sl.Add("Emoji");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "CircleMigrationsV202608261644", MigrationVersion);
            await CheckSqlTableVersion(cn, "Circle", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO CircleMigrationsV202608261644 (rowId,identityId,circleId,circleName,data,AppId,GrantOn,Designation,Emoji) " +
               $"SELECT rowId,identityId,circleId,circleName,data,AppId,GrantOn,Designation,Emoji "+
               $"FROM Circle;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202608261644
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "Circle", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "CircleMigrationsV202608261644", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "Circle", "CircleMigrationsV202608261644") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "Circle", $"CircleMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "CircleMigrationsV202608261644", "Circle");
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
                    await SqlHelper.RenameAsync(cn, "Circle", "CircleMigrationsV202608261644");
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
