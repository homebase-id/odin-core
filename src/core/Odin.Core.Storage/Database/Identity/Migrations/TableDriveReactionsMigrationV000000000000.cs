using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory;

// THIS FILE WAS INITIALLY AUTO GENERATED

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableDriveReactionsMigrationV0 : MigrationBase
{
    public TableDriveReactionsMigrationV0(long previousVersion) : base(previousVersion)
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
            commentSql = "COMMENT ON TABLE DriveReactionsMigrationsV0 IS '{ \"Version\": 0 }';";
        }
        else
        {
            rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
        }

        var wori = "";
        var createSql =
                "CREATE TABLE IF NOT EXISTS DriveReactionsMigrationsV0( -- { \"Version\": 0 }\n"
                + rowid
                + "identityId BYTEA NOT NULL, "
                + "driveId BYTEA NOT NULL, "
                + "postId BYTEA NOT NULL, "
                + "identity TEXT NOT NULL, "
                + "singleReaction TEXT NOT NULL "
                + ", UNIQUE(identityId,driveId,postId,identity,singleReaction)"
                + $"){wori};"
            ;
        await SqlHelper.CreateTableWithCommentAsync(cn, "DriveReactionsMigrationsV0", createSql, commentSql);
    }

    public new static List<string> GetColumnNames()
    {
        var sl = new List<string>();
        sl.Add("rowId");
        sl.Add("identityId");
        sl.Add("driveId");
        sl.Add("postId");
        sl.Add("identity");
        sl.Add("singleReaction");
        return sl;
    }

    public async Task<int> CopyDataAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "DriveReactionsMigrationsV0", MigrationVersion);
        await CheckSqlTableVersion(cn, "DriveReactions", PreviousVersion);
        await using var copyCommand = cn.CreateCommand();
        {
            copyCommand.CommandText =
                "INSERT INTO DriveReactionsMigrationsV0 (rowId,identityId,driveId,postId,identity,singleReaction) " +
                "SELECT rowId,identityId,driveId,postId,identity,singleReaction " +
                "FROM DriveReactions;";
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
            await SqlHelper.RenameAsync(cn, "DriveReactionsMigrationsV0", "DriveReactions");
            trn.Commit();
        }
    }

    public override async Task DownAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "DriveReactions", MigrationVersion);
        throw new Exception("You cannot move down from version 0");
    }
}