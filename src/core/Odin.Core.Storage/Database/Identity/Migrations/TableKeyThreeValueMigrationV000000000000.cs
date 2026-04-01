using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory;

// THIS FILE WAS INITIALLY AUTO GENERATED

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableKeyThreeValueMigrationV0 : MigrationBase
{
    public TableKeyThreeValueMigrationV0(long previousVersion) : base(previousVersion)
    {
    }

    public override long MigrationVersion => 0;

    public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
    {
        var rowid = "";
        var commentSql = "";
        if (cn.DatabaseType == DatabaseType.Postgres)
        {
            rowid = "rowid BIGSERIAL PRIMARY KEY,";
            commentSql = "COMMENT ON TABLE KeyThreeValueMigrationsV0 IS '{ \"Version\": 0 }';";
        }
        else
        {
            rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
        }

        var wori = "";
        var createSql =
                "CREATE TABLE IF NOT EXISTS KeyThreeValueMigrationsV0( -- { \"Version\": 0 }\n"
                + rowid
                + "identityId BYTEA NOT NULL, "
                + "key1 BYTEA NOT NULL, "
                + "key2 BYTEA , "
                + "key3 BYTEA , "
                + "data BYTEA  "
                + ", UNIQUE(identityId,key1)"
                + $"){wori};"
                + "CREATE INDEX IF NOT EXISTS Idx0KeyThreeValueMigrationsV0 ON KeyThreeValueMigrationsV0(identityId,key2);"
                + "CREATE INDEX IF NOT EXISTS Idx1KeyThreeValueMigrationsV0 ON KeyThreeValueMigrationsV0(key3);"
            ;
        await SqlHelper.CreateTableWithCommentAsync(cn, "KeyThreeValueMigrationsV0", createSql, commentSql);
    }

    public new static List<string> GetColumnNames()
    {
        var sl = new List<string>();
        sl.Add("rowId");
        sl.Add("identityId");
        sl.Add("key1");
        sl.Add("key2");
        sl.Add("key3");
        sl.Add("data");
        return sl;
    }

    public async Task<int> CopyDataAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "KeyThreeValueMigrationsV0", MigrationVersion);
        await CheckSqlTableVersion(cn, "KeyThreeValue", PreviousVersion);
        await using var copyCommand = cn.CreateCommand();
        {
            copyCommand.CommandText = "INSERT INTO KeyThreeValueMigrationsV0 (rowId,identityId,key1,key2,key3,data) " +
                                      "SELECT rowId,identityId,key1,key2,key3,data " +
                                      "FROM KeyThreeValue;";
            return await copyCommand.ExecuteNonQueryAsync();
        }
    }

    // DriveMainIndex is presumed to be the previous version
    // Will upgrade from the previous version to version 0
    public override async Task UpAsync(IConnectionWrapper cn)
    {
        using (var trn = await cn.BeginStackedTransactionAsync())
        {
            // Create the initial table
            await CreateTableWithCommentAsync(cn);
            await SqlHelper.RenameAsync(cn, "KeyThreeValueMigrationsV0", "KeyThreeValue");
            trn.Commit();
        }
    }

    public override async Task DownAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "KeyThreeValue", MigrationVersion);
        throw new Exception("You cannot move down from version 0");
    }
}