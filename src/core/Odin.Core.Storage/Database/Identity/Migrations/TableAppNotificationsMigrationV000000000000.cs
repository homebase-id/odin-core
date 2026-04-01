using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory;

// THIS FILE WAS INITIALLY AUTO GENERATED

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableAppNotificationsMigrationV0 : MigrationBase
{
    public TableAppNotificationsMigrationV0(long previousVersion) : base(previousVersion)
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
            commentSql = "COMMENT ON TABLE AppNotificationsMigrationsV0 IS '{ \"Version\": 0 }';";
        }
        else
        {
            rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
        }

        var wori = "";
        var createSql =
                "CREATE TABLE IF NOT EXISTS AppNotificationsMigrationsV0( -- { \"Version\": 0 }\n"
                + rowid
                + "identityId BYTEA NOT NULL, "
                + "notificationId BYTEA NOT NULL UNIQUE, "
                + "unread BIGINT NOT NULL, "
                + "senderId TEXT , "
                + "timestamp BIGINT NOT NULL, "
                + "data BYTEA , "
                + "created BIGINT NOT NULL, "
                + "modified BIGINT NOT NULL "
                + ", UNIQUE(identityId,notificationId)"
                + $"){wori};"
                + "CREATE INDEX IF NOT EXISTS Idx0AppNotificationsMigrationsV0 ON AppNotificationsMigrationsV0(identityId,created);"
            ;
        await SqlHelper.CreateTableWithCommentAsync(cn, "AppNotificationsMigrationsV0", createSql, commentSql);
    }

    public new static List<string> GetColumnNames()
    {
        var sl = new List<string>();
        sl.Add("rowId");
        sl.Add("identityId");
        sl.Add("notificationId");
        sl.Add("unread");
        sl.Add("senderId");
        sl.Add("timestamp");
        sl.Add("data");
        sl.Add("created");
        sl.Add("modified");
        return sl;
    }

    public async Task<int> CopyDataAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "AppNotificationsMigrationsV0", MigrationVersion);
        await CheckSqlTableVersion(cn, "AppNotifications", PreviousVersion);
        await using var copyCommand = cn.CreateCommand();
        {
            copyCommand.CommandText =
                "INSERT INTO AppNotificationsMigrationsV0 (rowId,identityId,notificationId,unread,senderId,timestamp,data,created,modified) " +
                "SELECT rowId,identityId,notificationId,unread,senderId,timestamp,data,created,modified " +
                "FROM AppNotifications;";
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
            await SqlHelper.RenameAsync(cn, "AppNotificationsMigrationsV0", "AppNotifications");
            trn.Commit();
        }
    }

    public override async Task DownAsync(IConnectionWrapper cn)
    {
        await CheckSqlTableVersion(cn, "AppNotifications", MigrationVersion);
        throw new Exception("You cannot move down from version 0");
    }
}