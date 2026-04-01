using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory;

// THIS FILE WAS INITIALLY AUTO GENERATED

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableImFollowingMigrationV0 : MigrationBase
{
    public TableImFollowingMigrationV0(long previousVersion) : base(previousVersion)
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
            commentSql = "COMMENT ON TABLE ImFollowingMigrationsV0 IS '{ \"Version\": 0 }';";
        }
        else
        {
            rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
        }

        var wori = "";
        var createSql =
                "CREATE TABLE IF NOT EXISTS ImFollowingMigrationsV0( -- { \"Version\": 0 }\n"
                + rowid
                + "identityId BYTEA NOT NULL, "
                + "identity TEXT NOT NULL, "
                + "driveId BYTEA NOT NULL, "
                + "created BIGINT NOT NULL, "
                + "modified BIGINT NOT NULL "
                + ", UNIQUE(identityId,identity,driveId)"
                + $"){wori};"
                + "CREATE INDEX IF NOT EXISTS Idx0ImFollowingMigrationsV0 ON ImFollowingMigrationsV0(identityId,identity);"
            ;
        await SqlHelper.CreateTableWithCommentAsync(cn, "ImFollowingMigrationsV0", createSql, commentSql);
    }

    public new static List<string> GetColumnNames()
    {
        var sl = new List<string>();
        sl.Add("rowId");
        sl.Add("identityId");
        sl.Add("identity");
        sl.Add("driveId");
        sl.Add("created");
        sl.Add("modified");
        return sl;
    }

    public async Task<int> CopyDataAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "ImFollowingMigrationsV0", MigrationVersion);
        await CheckSqlTableVersion(cn, "ImFollowing", PreviousVersion);
        await using var copyCommand = cn.CreateCommand();
        {
            copyCommand.CommandText =
                "INSERT INTO ImFollowingMigrationsV0 (rowId,identityId,identity,driveId,created,modified) " +
                "SELECT rowId,identityId,identity,driveId,created,modified " +
                "FROM ImFollowing;";
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
            await SqlHelper.RenameAsync(cn, "ImFollowingMigrationsV0", "ImFollowing");
            trn.Commit();
        }
    }

    public override async Task DownAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "ImFollowing", MigrationVersion);
        throw new Exception("You cannot move down from version 0");
    }
}