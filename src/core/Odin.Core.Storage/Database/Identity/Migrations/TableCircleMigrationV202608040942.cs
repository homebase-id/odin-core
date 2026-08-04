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
    public class TableCircleMigrationV202608040942 : MigrationBase
    {
        public override Int64 MigrationVersion => 202608040942;
        public TableCircleMigrationV202608040942(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE CircleMigrationsV202608040942 IS '{ \"Version\": 202608040942 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS CircleMigrationsV202608040942( -- { \"Version\": 202608040942 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"circleId BYTEA NOT NULL UNIQUE, "
                   +"circleName TEXT NOT NULL, "
                   +"data BYTEA , "
                   +"AppId BYTEA , "
                   +"Enrollment BIGINT NOT NULL DEFAULT 0, "
                   +"Designation BIGINT NOT NULL DEFAULT 1, "
                   +"Emoji TEXT  "
                   +", UNIQUE(identityId,circleId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0CircleMigrationsV202608040942 ON CircleMigrationsV202608040942(identityId,AppId);"
                   +"CREATE INDEX IF NOT EXISTS Idx1CircleMigrationsV202608040942 ON CircleMigrationsV202608040942(identityId,Enrollment);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "CircleMigrationsV202608040942", createSql, commentSql);
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
            sl.Add("Enrollment");
            sl.Add("Designation");
            sl.Add("Emoji");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "CircleMigrationsV202608040942", MigrationVersion);
            await CheckSqlTableVersion(cn, "Circle", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                // AppId, Enrollment, Designation and Emoji are new in this version and are omitted
                // from both lists: they do not exist on the old table. AppId and Emoji are nullable
                // (NULL = owner circle / no emoji); Enrollment and Designation take their column
                // DEFAULTs of 0 (NONE) and 1 (PERSONAL), which preserve today's behaviour.
                copyCommand.CommandText = "INSERT INTO CircleMigrationsV202608040942 (rowId,identityId,circleId,circleName,data) " +
               $"SELECT rowId,identityId,circleId,circleName,data "+
               $"FROM Circle;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202608040942
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "Circle", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "CircleMigrationsV202608040942", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "Circle", "CircleMigrationsV202608040942") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "Circle", $"CircleMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "CircleMigrationsV202608040942", "Circle");
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
                    await SqlHelper.RenameAsync(cn, "Circle", "CircleMigrationsV202608040942");
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
