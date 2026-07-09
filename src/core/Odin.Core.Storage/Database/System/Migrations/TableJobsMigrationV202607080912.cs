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
    public class TableJobsMigrationV202607080912 : MigrationBase
    {
        public override Int64 MigrationVersion => 202607080912;
        public TableJobsMigrationV202607080912(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE JobsMigrationsV202607080912 IS '{ \"Version\": 202607080912 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS JobsMigrationsV202607080912( -- { \"Version\": 202607080912 }\n"
                   +rowid
                   +"id BYTEA NOT NULL UNIQUE, "
                   +"name TEXT NOT NULL, "
                   +"state BIGINT NOT NULL, "
                   +"priority BIGINT NOT NULL, "
                   +"nextRun BIGINT NOT NULL, "
                   +"lastRun BIGINT , "
                   +"runCount BIGINT NOT NULL, "
                   +"maxAttempts BIGINT NOT NULL, "
                   +"retryDelay BIGINT NOT NULL, "
                   +"onSuccessDeleteAfter BIGINT NOT NULL, "
                   +"onFailureDeleteAfter BIGINT NOT NULL, "
                   +"expiresAt BIGINT , "
                   +"correlationId TEXT NOT NULL, "
                   +"jobType TEXT NOT NULL, "
                   +"jobData TEXT , "
                   +"jobHash TEXT  UNIQUE, "
                   +"lastError TEXT , "
                   +"identityId BYTEA , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0JobsMigrationsV202607080912 ON JobsMigrationsV202607080912(state);"
                   +"CREATE INDEX IF NOT EXISTS Idx1JobsMigrationsV202607080912 ON JobsMigrationsV202607080912(expiresAt);"
                   +"CREATE INDEX IF NOT EXISTS Idx2JobsMigrationsV202607080912 ON JobsMigrationsV202607080912(nextRun,priority);"
                   +"CREATE INDEX IF NOT EXISTS Idx3JobsMigrationsV202607080912 ON JobsMigrationsV202607080912(identityId);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "JobsMigrationsV202607080912", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("id");
            sl.Add("name");
            sl.Add("state");
            sl.Add("priority");
            sl.Add("nextRun");
            sl.Add("lastRun");
            sl.Add("runCount");
            sl.Add("maxAttempts");
            sl.Add("retryDelay");
            sl.Add("onSuccessDeleteAfter");
            sl.Add("onFailureDeleteAfter");
            sl.Add("expiresAt");
            sl.Add("correlationId");
            sl.Add("jobType");
            sl.Add("jobData");
            sl.Add("jobHash");
            sl.Add("lastError");
            sl.Add("identityId");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "JobsMigrationsV202607080912", MigrationVersion);
            await CheckSqlTableVersion(cn, "Jobs", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO JobsMigrationsV202607080912 (rowId,id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
               $"SELECT rowId,id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified "+
               $"FROM Jobs;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202607080912
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "Jobs", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "JobsMigrationsV202607080912", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "Jobs", "JobsMigrationsV202607080912") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "Jobs", $"JobsMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "JobsMigrationsV202607080912", "Jobs");
                    await CheckSqlTableVersion(cn, "Jobs", MigrationVersion);
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
            await CheckSqlTableVersion(cn, "Jobs", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"JobsMigrationsV{PreviousVersion}", "Jobs") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "Jobs", "JobsMigrationsV202607080912");
                    await SqlHelper.RenameAsync(cn, $"JobsMigrationsV{PreviousVersion}", "Jobs");
                    await CheckSqlTableVersion(cn, "Jobs", PreviousVersion);
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
