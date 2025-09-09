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

namespace Odin.Core.Storage.Database.System.Migrations
{
    public class TableRegistrationsMigrationV202509090509 : MigrationBase
    {
        public override Int64 MigrationVersion => 202509090509;
        public TableRegistrationsMigrationV202509090509(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE RegistrationsMigrationsV202509090509 IS '{ \"Version\": 202509090509 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS RegistrationsMigrationsV202509090509( -- { \"Version\": 202509090509 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL UNIQUE, "
                   +"email TEXT , "
                   +"primaryDomainName TEXT NOT NULL UNIQUE, "
                   +"firstRunToken TEXT , "
                   +"disabled BOOLEAN NOT NULL, "
                   +"markedForDeletionDate BIGINT , "
                   +"planId TEXT , "
                   +"json TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "RegistrationsMigrationsV202509090509", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("email");
            sl.Add("primaryDomainName");
            sl.Add("firstRunToken");
            sl.Add("disabled");
            sl.Add("markedForDeletionDate");
            sl.Add("planId");
            sl.Add("json");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "RegistrationsMigrationsV202509090509", MigrationVersion);
            await CheckSqlTableVersion(cn, "Registrations", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO RegistrationsMigrationsV202509090509 (rowId,identityId,email,primaryDomainName,firstRunToken,disabled,markedForDeletionDate,planId,json,created,modified) " +
               $"SELECT rowId,identityId,email,primaryDomainName,firstRunToken,disabled,markedForDeletionDate,planId,json,created,modified "+
               $"FROM Registrations;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202509090509
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "Registrations", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "RegistrationsMigrationsV202509090509", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "Registrations", "RegistrationsMigrationsV202509090509") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "Registrations", $"RegistrationsMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "RegistrationsMigrationsV202509090509", "Registrations");
                    await CheckSqlTableVersion(cn, "Registrations", MigrationVersion);
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
            await CheckSqlTableVersion(cn, "Registrations", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"RegistrationsMigrationsV{PreviousVersion}", "Registrations") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "Registrations", "RegistrationsMigrationsV202509090509");
                    await SqlHelper.RenameAsync(cn, $"RegistrationsMigrationsV{PreviousVersion}", "Registrations");
                    await CheckSqlTableVersion(cn, "Registrations", PreviousVersion);
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
