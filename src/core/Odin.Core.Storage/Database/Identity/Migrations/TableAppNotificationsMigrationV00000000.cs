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

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class TableAppNotificationsMigrationV0 : MigrationBase
    {
        public override int MigrationVersion => 0;
        public TableAppNotificationsMigrationV0(MigrationListBase container) : base(container)
        {
        }

        public virtual async Task<int> EnsureTableExistsAsync(IConnectionWrapper cn, bool dropExisting = false)
        {
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS AppNotificationsMigrationsV0;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS AppNotificationsMigrationsV0("
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"notificationId BYTEA NOT NULL UNIQUE, "
                   +"unread BIGINT NOT NULL, "
                   +"senderId TEXT , "
                   +"timestamp BIGINT NOT NULL, "
                   +"data BYTEA , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,notificationId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0AppNotificationsMigrationsV0 ON AppNotificationsMigrationsV0(identityId,created);"
                   ;
            return await cmd.ExecuteNonQueryAsync();
        }

        public static List<string> GetColumnNames()
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
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO AppNotificationsMigrationsV0 (rowId,identityId,notificationId,unread,senderId,timestamp,data,created,modified) " +
               $"SELECT rowId,identityId,notificationId,unread,senderId,timestamp,data,created,modified "+
               $"FROM AppNotifications;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // DriveMainIndex is presumed to be the previous version
        // Will upgrade from the previous version to version 0
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await Task.Delay(0);
            throw new  Exception("You cannot move up from version 0");
        }

        public override async Task DownAsync(IConnectionWrapper cn)
        {
            await Task.Delay(0);
            throw new  Exception("You cannot move down from version 0");
        }

    }
}
