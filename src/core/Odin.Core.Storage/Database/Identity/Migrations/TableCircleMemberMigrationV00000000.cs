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
    public class TableCircleMemberMigrationV0 : MigrationBase
    {
        public override int MigrationVersion => 0;
        public TableCircleMemberMigrationV0(MigrationListBase container) : base(container)
        {
        }

        public virtual async Task<int> EnsureTableExistsAsync(IConnectionWrapper cn, bool dropExisting = false)
        {
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS CircleMemberMigrationsV0;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS CircleMemberMigrationsV0("
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"circleId BYTEA NOT NULL, "
                   +"memberId BYTEA NOT NULL, "
                   +"data BYTEA  "
                   +", UNIQUE(identityId,circleId,memberId)"
                   +$"){wori};"
                   ;
            return await cmd.ExecuteNonQueryAsync();
        }

        public static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("circleId");
            sl.Add("memberId");
            sl.Add("data");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO CircleMemberMigrationsV0 (rowId,identityId,circleId,memberId,data) " +
               $"SELECT rowId,identityId,circleId,memberId,data "+
               $"FROM CircleMember;";
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
