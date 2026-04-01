using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableClientRegistrationsMigrationV202510201056(long previousVersion) : MigrationBase(previousVersion)
{
    public override long MigrationVersion => 202510201056;

    public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
    {
        var rowid = "";
        var commentSql = "";
        if (cn.DatabaseType == DatabaseType.Postgres)
        {
            rowid = "rowId BIGSERIAL PRIMARY KEY,";
            commentSql =
                "COMMENT ON TABLE ClientRegistrationsMigrationsV202510201056 IS '{ \"Version\": 202510201056 }';";
        }
        else
        {
            rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
        }

        var wori = "";
        var createSql =
                "CREATE TABLE IF NOT EXISTS ClientRegistrationsMigrationsV202510201056( -- { \"Version\": 202510201056 }\n"
                + rowid
                + "identityId BYTEA NOT NULL, "
                + "catId BYTEA NOT NULL UNIQUE, "
                + "issuedToId TEXT NOT NULL, "
                + "ttl BIGINT NOT NULL, "
                + "expiresAt BIGINT NOT NULL, "
                + "categoryId BYTEA NOT NULL, "
                + "catType BIGINT NOT NULL, "
                + "value TEXT , "
                + "created BIGINT NOT NULL, "
                + "modified BIGINT NOT NULL "
                + ", UNIQUE(identityId,catId)"
                + $"){wori};"
            ;
        await SqlHelper.CreateTableWithCommentAsync(cn, "ClientRegistrationsMigrationsV202510201056", createSql,
            commentSql);
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
        sl.Add("categoryId");
        sl.Add("catType");
        sl.Add("value");
        sl.Add("created");
        sl.Add("modified");
        return sl;
    }

    public Task<int> CopyDataAsync(IConnectionWrapper cn)
    {
        throw new NotImplementedException();
    }

    // Will upgrade from the previous version to version 202510201056
    public override async Task UpAsync(IConnectionWrapper cn)
    {
        await using var trn = await cn.BeginStackedTransactionAsync();
        await CreateTableWithCommentAsync(cn);
        await SqlHelper.RenameAsync(cn, "ClientRegistrationsMigrationsV202510201056", "ClientRegistrations");
        trn.Commit();
    }

    public override async Task DownAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "ClientRegistrations", MigrationVersion);
        await SqlHelper.DeleteTableAsync(cn, "ClientRegistrations");
    }
}