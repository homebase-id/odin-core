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
    public class TableConnectionsMigrationV202608040942 : MigrationBase
    {
        public override Int64 MigrationVersion => 202608040942;
        public TableConnectionsMigrationV202608040942(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE ConnectionsMigrationsV202608040942 IS '{ \"Version\": 202608040942 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS ConnectionsMigrationsV202608040942( -- { \"Version\": 202608040942 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"identity TEXT NOT NULL, "
                   +"displayName TEXT NOT NULL, "
                   +"status BIGINT NOT NULL, "
                   +"accessIsRevoked BIGINT NOT NULL, "
                   +"data BYTEA , "
                   +"ReviewedAt BIGINT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,identity)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0ConnectionsMigrationsV202608040942 ON ConnectionsMigrationsV202608040942(identityId,created);"
                   +"CREATE INDEX IF NOT EXISTS Idx1ConnectionsMigrationsV202608040942 ON ConnectionsMigrationsV202608040942(identityId,status,ReviewedAt);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "ConnectionsMigrationsV202608040942", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("identity");
            sl.Add("displayName");
            sl.Add("status");
            sl.Add("accessIsRevoked");
            sl.Add("data");
            sl.Add("ReviewedAt");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "ConnectionsMigrationsV202608040942", MigrationVersion);
            await CheckSqlTableVersion(cn, "Connections", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                // ReviewedAt is new in this version and is omitted from both lists: it does not
                // exist on the old table and is nullable, so every existing connection lands as
                // NULL, i.e. "New / not yet reviewed".
                copyCommand.CommandText = "INSERT INTO ConnectionsMigrationsV202608040942 (rowId,identityId,identity,displayName,status,accessIsRevoked,data,created,modified) " +
               $"SELECT rowId,identityId,identity,displayName,status,accessIsRevoked,data,created,modified "+
               $"FROM Connections;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202608040942
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "Connections", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "ConnectionsMigrationsV202608040942", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "Connections", "ConnectionsMigrationsV202608040942") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "Connections", $"ConnectionsMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "ConnectionsMigrationsV202608040942", "Connections");
                    await CheckSqlTableVersion(cn, "Connections", MigrationVersion);
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
            await CheckSqlTableVersion(cn, "Connections", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"ConnectionsMigrationsV{PreviousVersion}", "Connections") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "Connections", "ConnectionsMigrationsV202608040942");
                    await SqlHelper.RenameAsync(cn, $"ConnectionsMigrationsV{PreviousVersion}", "Connections");
                    await CheckSqlTableVersion(cn, "Connections", PreviousVersion);
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
