using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory;

// THIS FILE WAS INITIALLY AUTO GENERATED

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableDriveAclIndexMigrationV0 : MigrationBase
{
    public TableDriveAclIndexMigrationV0(long previousVersion) : base(previousVersion)
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
            commentSql = "COMMENT ON TABLE DriveAclIndexMigrationsV0 IS '{ \"Version\": 0 }';";
        }
        else
        {
            rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
        }

        var wori = "";
        var createSql =
                "CREATE TABLE IF NOT EXISTS DriveAclIndexMigrationsV0( -- { \"Version\": 0 }\n"
                + rowid
                + "identityId BYTEA NOT NULL, "
                + "driveId BYTEA NOT NULL, "
                + "fileId BYTEA NOT NULL, "
                + "aclMemberId BYTEA NOT NULL "
                + ", UNIQUE(identityId,driveId,fileId,aclMemberId)"
                + $"){wori};"
            ;
        await SqlHelper.CreateTableWithCommentAsync(cn, "DriveAclIndexMigrationsV0", createSql, commentSql);
    }

    public new static List<string> GetColumnNames()
    {
        var sl = new List<string>();
        sl.Add("rowId");
        sl.Add("identityId");
        sl.Add("driveId");
        sl.Add("fileId");
        sl.Add("aclMemberId");
        return sl;
    }

    public async Task<int> CopyDataAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "DriveAclIndexMigrationsV0", MigrationVersion);
        await CheckSqlTableVersion(cn, "DriveAclIndex", PreviousVersion);
        await using var copyCommand = cn.CreateCommand();
        {
            copyCommand.CommandText =
                "INSERT INTO DriveAclIndexMigrationsV0 (rowId,identityId,driveId,fileId,aclMemberId) " +
                "SELECT rowId,identityId,driveId,fileId,aclMemberId " +
                "FROM DriveAclIndex;";
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
            await SqlHelper.RenameAsync(cn, "DriveAclIndexMigrationsV0", "DriveAclIndex");
            trn.Commit();
        }
    }

    public override async Task DownAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "DriveAclIndex", MigrationVersion);
        throw new Exception("You cannot move down from version 0");
    }
}