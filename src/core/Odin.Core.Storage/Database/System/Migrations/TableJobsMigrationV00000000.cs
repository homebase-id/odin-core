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

namespace Odin.Core.Storage.Database.System.Table
{
    public class TableJobsMigrationV0 : MigrationBase
    {
        public override int MigrationVersion => 0;
        public TableJobsMigrationV0(MigrationListBase container) : base(container)
        {
        }

        public virtual async Task<int> EnsureTableExistsAsync(IConnectionWrapper cn, bool dropExisting = false)
        {
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS JobsMigrationsV0;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS JobsMigrationsV0("
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
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0JobsMigrationsV0 ON JobsMigrationsV0(state);"
                   +"CREATE INDEX IF NOT EXISTS Idx1JobsMigrationsV0 ON JobsMigrationsV0(expiresAt);"
                   +"CREATE INDEX IF NOT EXISTS Idx2JobsMigrationsV0 ON JobsMigrationsV0(nextRun,priority);"
                   ;
            return await cmd.ExecuteNonQueryAsync();
        }

        public static List<string> GetColumnNames()
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
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO JobsMigrationsV0 (rowId,id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
               $"SELECT rowId,id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified "+
               $"FROM Jobs;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // DriveMainIndex is presumed to be the previous version
        // Will upgrade from the previous version to version 0
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await Task.Delay(0);
            throw new  Exception("You cannot move up from version 0");
        }

        public override async Task DownAsync(IConnectionWrapper cn)
        {
            await Task.Delay(0);
            throw new  Exception("You cannot move down from version 0");
        }

    }
}
