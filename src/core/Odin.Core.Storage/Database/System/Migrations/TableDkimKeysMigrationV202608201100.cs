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
using Odin.Core.Storage.Database.System.Connection;

#nullable disable

// THIS FILE WAS INITIALLY AUTO GENERATED

namespace Odin.Core.Storage.Database.System.Migrations
{
    public class TableDkimKeysMigrationV202608201100 : MigrationBase
    {
        public override Int64 MigrationVersion => 202608201100;
        public TableDkimKeysMigrationV202608201100(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DkimKeysMigrationsV202608201100 IS '{ \"Version\": 202608201100 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS DkimKeysMigrationsV202608201100( -- { \"Version\": 202608201100 }\n"
                   +rowid
                   +"domain TEXT NOT NULL, "
                   +"selector TEXT NOT NULL, "
                   +"algorithm TEXT NOT NULL, "
                   +"publicKey TEXT NOT NULL, "
                   +"privateKey TEXT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(domain,selector)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "DkimKeysMigrationsV202608201100", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("domain");
            sl.Add("selector");
            sl.Add("algorithm");
            sl.Add("publicKey");
            sl.Add("privateKey");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "DkimKeysMigrationsV202608201100", MigrationVersion);
            await CheckSqlTableVersion(cn, "DkimKeys", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO DkimKeysMigrationsV202608201100 (rowId,domain,selector,algorithm,publicKey,privateKey,created,modified) " +
               $"SELECT rowId,domain,selector,algorithm,publicKey,privateKey,created,modified "+
               $"FROM DkimKeys;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202608201100
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "DkimKeys", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "DkimKeysMigrationsV202608201100", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "DkimKeys", "DkimKeysMigrationsV202608201100") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "DkimKeys", $"DkimKeysMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "DkimKeysMigrationsV202608201100", "DkimKeys");
                    await CheckSqlTableVersion(cn, "DkimKeys", MigrationVersion);
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
            await CheckSqlTableVersion(cn, "DkimKeys", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"DkimKeysMigrationsV{PreviousVersion}", "DkimKeys") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "DkimKeys", "DkimKeysMigrationsV202608201100");
                    await SqlHelper.RenameAsync(cn, $"DkimKeysMigrationsV{PreviousVersion}", "DkimKeys");
                    await CheckSqlTableVersion(cn, "DkimKeys", PreviousVersion);
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
