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

namespace Odin.Core.Storage.Database.KeyChain.Migrations
{
    public class TableKeyChainMigrationV0 : MigrationBase
    {
        public override Int64 MigrationVersion => 0;
        public TableKeyChainMigrationV0(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE KeyChainMigrationsV0 IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS KeyChainMigrationsV0( -- { \"Version\": 0 }\n"
                   +rowid
                   +"previousHash BYTEA NOT NULL UNIQUE, "
                   +"identity TEXT NOT NULL, "
                   +"timestamp BIGINT NOT NULL, "
                   +"signedPreviousHash BYTEA NOT NULL UNIQUE, "
                   +"algorithm TEXT NOT NULL, "
                   +"publicKeyJwkBase64Url TEXT NOT NULL UNIQUE, "
                   +"recordHash BYTEA NOT NULL UNIQUE "
                   +", UNIQUE(identity,publicKeyJwkBase64Url)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "KeyChainMigrationsV0", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("previousHash");
            sl.Add("identity");
            sl.Add("timestamp");
            sl.Add("signedPreviousHash");
            sl.Add("algorithm");
            sl.Add("publicKeyJwkBase64Url");
            sl.Add("recordHash");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "KeyChainMigrationsV0", MigrationVersion);
            await CheckSqlTableVersion(cn, "KeyChain", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO KeyChainMigrationsV0 (rowId,previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash) " +
               $"SELECT rowId,previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash "+
               $"FROM KeyChain;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // DriveMainIndex is presumed to be the previous version
        // Will upgrade from the previous version to version 0
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            // Create the initial table
            await CreateTableWithCommentAsync(cn);
        }

        public override async Task DownAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "KeyChain", MigrationVersion);
            throw new  Exception("You cannot move down from version 0");
        }

    }
}
