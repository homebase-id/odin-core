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
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;

// THIS FILE WAS INITIALLY AUTO GENERATED

namespace Odin.Core.Storage.Database.Identity.Migrations
{
    public class TableImFollowingMigrationV202603141230 : MigrationBase
    {
        public override Int64 MigrationVersion => 202603141230;
        public TableImFollowingMigrationV202603141230(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE ImFollowingMigrationsV202603141230 IS '{ \"Version\": 202603141230 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS ImFollowingMigrationsV202603141230( -- { \"Version\": 202603141230 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"sourceOdinId TEXT NOT NULL, "
                   +"sourceDriveId BYTEA , "
                   +"sourceDriveTypeId BYTEA , "
                   +"targetDriveId BYTEA , "
                   +"subscriptionKind BIGINT NOT NULL, "
                   +"lastNotification BIGINT NOT NULL, "
                   +"lastQuery BIGINT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,sourceOdinId,sourceDriveId,sourceDriveTypeId,targetDriveId)"
                   +", CHECK ((sourceDriveId IS NOT NULL AND sourceDriveTypeId IS NULL) OR (sourceDriveId IS NULL AND sourceDriveTypeId IS NOT NULL))"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0ImFollowingMigrationsV202603141230 ON ImFollowingMigrationsV202603141230(identityId,sourceOdinId);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "ImFollowingMigrationsV202603141230", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("sourceOdinId");
            sl.Add("sourceDriveId");
            sl.Add("sourceDriveTypeId");
            sl.Add("targetDriveId");
            sl.Add("subscriptionKind");
            sl.Add("lastNotification");
            sl.Add("lastQuery");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "ImFollowingMigrationsV202603141230", MigrationVersion);
            await CheckSqlTableVersion(cn, "ImFollowing", PreviousVersion);

            string emptyGuid        = Guid.Empty.BytesToSql(cn.DatabaseType);
            string channelDriveType = FollowsSubscriptionConstants.ChannelDriveType.BytesToSql(cn.DatabaseType);
            string feedDriveAlias   = FollowsSubscriptionConstants.FeedDriveAlias.BytesToSql(cn.DatabaseType);

            await using var copyCommand = cn.CreateCommand();
            {
                // Old schema: (rowId, identityId, identity, driveId, created, modified)
                // New schema: (rowId, identityId, sourceOdinId, sourceDriveId, sourceDriveTypeId,
                //              targetDriveId, subscriptionKind, lastNotification, lastQuery, created, modified)
                // driveId = Guid.Empty  → AllNotifications: sourceDriveTypeId=ChannelDriveType, sourceDriveId=NULL
                // driveId != Guid.Empty → SelectedChannels: sourceDriveId=driveId, sourceDriveTypeId=NULL
                copyCommand.CommandText =
                    "INSERT INTO ImFollowingMigrationsV202603141230 " +
                    "(rowId,identityId,sourceOdinId,sourceDriveId,sourceDriveTypeId,targetDriveId,subscriptionKind,lastNotification,lastQuery,created,modified) " +
                    "SELECT rowId, identityId, identity, " +
                    $"CASE WHEN driveId = {emptyGuid} THEN NULL ELSE driveId END, " +
                    $"CASE WHEN driveId = {emptyGuid} THEN {channelDriveType} ELSE NULL END, " +
                    $"{feedDriveAlias}, " +
                    $"CASE WHEN driveId = {emptyGuid} THEN 1 ELSE 2 END, " +
                    "0, 0, created, modified " +
                    "FROM ImFollowing;";
                return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202603141230
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "ImFollowing", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "ImFollowingMigrationsV202603141230", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "ImFollowing", "ImFollowingMigrationsV202603141230") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "ImFollowing", $"ImFollowingMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "ImFollowingMigrationsV202603141230", "ImFollowing");
                    await CheckSqlTableVersion(cn, "ImFollowing", MigrationVersion);
                    trn.Commit();
                }
            }
            catch
            {
                throw;
            }
        }

        public override async Task DownAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "ImFollowing", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"ImFollowingMigrationsV{PreviousVersion}", "ImFollowing") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "ImFollowing", "ImFollowingMigrationsV202603141230");
                    await SqlHelper.RenameAsync(cn, $"ImFollowingMigrationsV{PreviousVersion}", "ImFollowing");
                    await CheckSqlTableVersion(cn, "ImFollowing", PreviousVersion);
                    trn.Commit();
                }
            }
            catch
            {
                throw;
            }
        }

    }
}
