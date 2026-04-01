using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory;

// THIS FILE WAS INITIALLY AUTO GENERATED

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableNonceMigrationV0 : MigrationBase
{
    public TableNonceMigrationV0(long previousVersion) : base(previousVersion)
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
            commentSql = "COMMENT ON TABLE NonceMigrationsV0 IS '{ \"Version\": 0 }';";
        }
        else
        {
            rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
        }

        var wori = "";
        var createSql =
                "CREATE TABLE IF NOT EXISTS NonceMigrationsV0( -- { \"Version\": 0 }\n"
                + rowid
                + "identityId BYTEA NOT NULL, "
                + "id BYTEA NOT NULL UNIQUE, "
                + "expiration BIGINT NOT NULL, "
                + "data TEXT NOT NULL, "
                + "created BIGINT NOT NULL, "
                + "modified BIGINT NOT NULL "
                + ", UNIQUE(identityId,id)"
                + $"){wori};"
            ;
        await SqlHelper.CreateTableWithCommentAsync(cn, "NonceMigrationsV0", createSql, commentSql);
    }

    public new static List<string> GetColumnNames()
    {
        var sl = new List<string>();
        sl.Add("rowId");
        sl.Add("identityId");
        sl.Add("id");
        sl.Add("expiration");
        sl.Add("data");
        sl.Add("created");
        sl.Add("modified");
        return sl;
    }

    public async Task<int> CopyDataAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "NonceMigrationsV0", MigrationVersion);
        await CheckSqlTableVersion(cn, "Nonce", PreviousVersion);
        await using var copyCommand = cn.CreateCommand();
        {
            copyCommand.CommandText =
                "INSERT INTO NonceMigrationsV0 (rowId,identityId,id,expiration,data,created,modified) " +
                "SELECT rowId,identityId,id,expiration,data,created,modified " +
                "FROM Nonce;";
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
            await SqlHelper.RenameAsync(cn, "NonceMigrationsV0", "Nonce");
            trn.Commit();
        }
    }

    public override async Task DownAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "Nonce", MigrationVersion);
        throw new Exception("You cannot move down from version 0");
    }
}