using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory;

// THIS FILE WAS INITIALLY AUTO GENERATED

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableConnectionsMigrationV0 : MigrationBase
{
    public TableConnectionsMigrationV0(long previousVersion) : base(previousVersion)
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
            commentSql = "COMMENT ON TABLE ConnectionsMigrationsV0 IS '{ \"Version\": 0 }';";
        }
        else
        {
            rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
        }

        var wori = "";
        var createSql =
                "CREATE TABLE IF NOT EXISTS ConnectionsMigrationsV0( -- { \"Version\": 0 }\n"
                + rowid
                + "identityId BYTEA NOT NULL, "
                + "identity TEXT NOT NULL, "
                + "displayName TEXT NOT NULL, "
                + "status BIGINT NOT NULL, "
                + "accessIsRevoked BIGINT NOT NULL, "
                + "data BYTEA , "
                + "created BIGINT NOT NULL, "
                + "modified BIGINT NOT NULL "
                + ", UNIQUE(identityId,identity)"
                + $"){wori};"
                + "CREATE INDEX IF NOT EXISTS Idx0ConnectionsMigrationsV0 ON ConnectionsMigrationsV0(identityId,created);"
            ;
        await SqlHelper.CreateTableWithCommentAsync(cn, "ConnectionsMigrationsV0", createSql, commentSql);
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
        sl.Add("created");
        sl.Add("modified");
        return sl;
    }

    public async Task<int> CopyDataAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "ConnectionsMigrationsV0", MigrationVersion);
        await CheckSqlTableVersion(cn, "Connections", PreviousVersion);
        await using var copyCommand = cn.CreateCommand();
        {
            copyCommand.CommandText =
                "INSERT INTO ConnectionsMigrationsV0 (rowId,identityId,identity,displayName,status,accessIsRevoked,data,created,modified) " +
                "SELECT rowId,identityId,identity,displayName,status,accessIsRevoked,data,created,modified " +
                "FROM Connections;";
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
            await SqlHelper.RenameAsync(cn, "ConnectionsMigrationsV0", "Connections");
            trn.Commit();
        }
    }

    public override async Task DownAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "Connections", MigrationVersion);
        throw new Exception("You cannot move down from version 0");
    }
}