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
    public class TableCertificatesMigrationV0 : MigrationBase
    {
        public override Int64 MigrationVersion => 0;
        public TableCertificatesMigrationV0(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableIfNotExistsAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE CertificatesMigrationsV0 IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE CertificatesMigrationsV0( -- { \"Version\": 0 }\n"
                   +rowid
                   +"domain TEXT NOT NULL UNIQUE, "
                   +"privateKey TEXT NOT NULL, "
                   +"certificate TEXT NOT NULL, "
                   +"expiration BIGINT NOT NULL, "
                   +"lastAttempt BIGINT NOT NULL, "
                   +"correlationId TEXT NOT NULL, "
                   +"lastError TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +$"){wori};"
                   ;
            await MigrationBase.CreateTableIfNotExistsAsync(cn, createSql, commentSql);
        }

        public static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("domain");
            sl.Add("privateKey");
            sl.Add("certificate");
            sl.Add("expiration");
            sl.Add("lastAttempt");
            sl.Add("correlationId");
            sl.Add("lastError");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "CertificatesMigrationsV0", MigrationVersion);
            await CheckSqlTableVersion(cn, "Certificates", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO CertificatesMigrationsV0 (rowId,domain,privateKey,certificate,expiration,lastAttempt,correlationId,lastError,created,modified) " +
               $"SELECT rowId,domain,privateKey,certificate,expiration,lastAttempt,correlationId,lastError,created,modified "+
               $"FROM Certificates;";
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
