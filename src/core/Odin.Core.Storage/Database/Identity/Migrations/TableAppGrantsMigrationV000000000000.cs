using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory;

// THIS FILE WAS INITIALLY AUTO GENERATED

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableAppGrantsMigrationV0 : MigrationBase
{
    public TableAppGrantsMigrationV0(long previousVersion) : base(previousVersion)
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
            commentSql = "COMMENT ON TABLE AppGrantsMigrationsV0 IS '{ \"Version\": 0 }';";
        }
        else
        {
            rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
        }

        var wori = "";
        var createSql =
                "CREATE TABLE IF NOT EXISTS AppGrantsMigrationsV0( -- { \"Version\": 0 }\n"
                + rowid
                + "identityId BYTEA NOT NULL, "
                + "odinHashId BYTEA NOT NULL, "
                + "appId BYTEA NOT NULL, "
                + "circleId BYTEA NOT NULL, "
                + "data BYTEA  "
                + ", UNIQUE(identityId,odinHashId,appId,circleId)"
                + $"){wori};"
            ;
        await SqlHelper.CreateTableWithCommentAsync(cn, "AppGrantsMigrationsV0", createSql, commentSql);
    }

    public new static List<string> GetColumnNames()
    {
        var sl = new List<string>();
        sl.Add("rowId");
        sl.Add("identityId");
        sl.Add("odinHashId");
        sl.Add("appId");
        sl.Add("circleId");
        sl.Add("data");
        return sl;
    }

    public async Task<int> CopyDataAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "AppGrantsMigrationsV0", MigrationVersion);
        await CheckSqlTableVersion(cn, "AppGrants", PreviousVersion);
        await using var copyCommand = cn.CreateCommand();
        {
            copyCommand.CommandText =
                "INSERT INTO AppGrantsMigrationsV0 (rowId,identityId,odinHashId,appId,circleId,data) " +
                "SELECT rowId,identityId,odinHashId,appId,circleId,data " +
                "FROM AppGrants;";
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
            await SqlHelper.RenameAsync(cn, "AppGrantsMigrationsV0", "AppGrants");
            trn.Commit();
        }
    }

    public override async Task DownAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "AppGrants", MigrationVersion);
        throw new Exception("You cannot move down from version 0");
    }
}