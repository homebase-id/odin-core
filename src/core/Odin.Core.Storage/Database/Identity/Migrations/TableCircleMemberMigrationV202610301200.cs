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
    public class TableCircleMemberMigrationV202610301200 : MigrationBase
    {
        public override Int64 MigrationVersion => 202610301200;
        public TableCircleMemberMigrationV202610301200(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE CircleMemberMigrationsV202610301200 IS '{ \"Version\": 202610301200 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS CircleMemberMigrationsV202610301200( -- { \"Version\": 202610301200 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"circleId BYTEA NOT NULL, "
                   +"memberId BYTEA NOT NULL, "
                   +"data BYTEA , "
                   +"sealedShareKey BYTEA  "
                   +", UNIQUE(identityId,circleId,memberId)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "CircleMemberMigrationsV202610301200", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("circleId");
            sl.Add("memberId");
            sl.Add("data");
            sl.Add("sealedShareKey");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "CircleMemberMigrationsV202610301200", MigrationVersion);
            await CheckSqlTableVersion(cn, "CircleMember", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                // sealedShareKey is new in this version and is omitted from both lists: every existing member lands
                // as NULL until a device of the owning app seals the circle key for them (docs/selective-share-plan.md).
                copyCommand.CommandText = "INSERT INTO CircleMemberMigrationsV202610301200 (rowId,identityId,circleId,memberId,data) " +
               $"SELECT rowId,identityId,circleId,memberId,data "+
               $"FROM CircleMember;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202610301200
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "CircleMember", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "CircleMemberMigrationsV202610301200", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "CircleMember", "CircleMemberMigrationsV202610301200") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "CircleMember", $"CircleMemberMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "CircleMemberMigrationsV202610301200", "CircleMember");
                    await CheckSqlTableVersion(cn, "CircleMember", MigrationVersion);
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
            await CheckSqlTableVersion(cn, "CircleMember", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"CircleMemberMigrationsV{PreviousVersion}", "CircleMember") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "CircleMember", "CircleMemberMigrationsV202610301200");
                    await SqlHelper.RenameAsync(cn, $"CircleMemberMigrationsV{PreviousVersion}", "CircleMember");
                    await CheckSqlTableVersion(cn, "CircleMember", PreviousVersion);
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
