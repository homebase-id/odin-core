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

namespace Odin.Core.Storage.Database.Identity.Migrations
{
    public class TableCatsMigrationV0 : MigrationBase
    {
        public override Int64 MigrationVersion => 0;
        public TableCatsMigrationV0(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE CatsMigrationsV0 IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS CatsMigrationsV0( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"catId BYTEA NOT NULL UNIQUE, "
                   +"issuedToId TEXT NOT NULL, "
                   +"ttl BIGINT NOT NULL, "
                   +"expiresAt BIGINT NOT NULL, "
                   +"catType BIGINT NOT NULL, "
                   +"value TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,catId)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "CatsMigrationsV0", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("catId");
            sl.Add("issuedToId");
            sl.Add("ttl");
            sl.Add("expiresAt");
            sl.Add("catType");
            sl.Add("value");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "CatsMigrationsV0", MigrationVersion);
            await CheckSqlTableVersion(cn, "Cats", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO CatsMigrationsV0 (rowId,identityId,catId,issuedToId,ttl,expiresAt,catType,value,created,modified) " +
               $"SELECT rowId,identityId,catId,issuedToId,ttl,expiresAt,catType,value,created,modified "+
               $"FROM Cats;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // DriveMainIndex is presumed to be the previous version
        // Will upgrade from the previous version to version 0
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    // Create the initial table
                    await CreateTableWithCommentAsync(cn);
                    await SqlHelper.RenameAsync(cn, "CatsMigrationsV0", "Cats");
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
            await CheckSqlTableVersion(cn, "Cats", MigrationVersion);
            throw new  Exception("You cannot move down from version 0");
        }

    }
}
