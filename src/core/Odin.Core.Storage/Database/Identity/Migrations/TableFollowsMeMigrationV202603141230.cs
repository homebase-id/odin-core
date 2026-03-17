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

[assembly: InternalsVisibleTo("DatabaseCommitTest")]

namespace Odin.Core.Storage.Database.Identity.Migrations
{
    public class TableFollowsMeMigrationV202603141230 : MigrationBase
    {
        public override Int64 MigrationVersion => 202603141230;
        public TableFollowsMeMigrationV202603141230(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE FollowsMeMigrationsV202603141230 IS '{ \"Version\": 202603141230 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS FollowsMeMigrationsV202603141230( -- { \"Version\": 202603141230 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"subscriberOdinId TEXT NOT NULL, "
                   +"sourceDriveId BYTEA , "
                   +"sourceDriveTypeId BYTEA , "
                   +"subscriberTargetDriveId BYTEA , "
                   +"subscriptionKind BIGINT NOT NULL, "
                   +"lastNotification BIGINT NOT NULL, "
                   +"lastQuery BIGINT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,subscriberOdinId,sourceDriveId,sourceDriveTypeId,subscriberTargetDriveId)"
                   +", CHECK ((sourceDriveId IS NOT NULL AND sourceDriveTypeId IS NULL) OR (sourceDriveId IS NULL AND sourceDriveTypeId IS NOT NULL))"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0FollowsMeMigrationsV202603141230 ON FollowsMeMigrationsV202603141230(identityId,subscriberOdinId);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "FollowsMeMigrationsV202603141230", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("subscriberOdinId");
            sl.Add("sourceDriveId");
            sl.Add("sourceDriveTypeId");
            sl.Add("subscriberTargetDriveId");
            sl.Add("subscriptionKind");
            sl.Add("lastNotification");
            sl.Add("lastQuery");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "FollowsMeMigrationsV202603141230", MigrationVersion);
            await CheckSqlTableVersion(cn, "FollowsMe", PreviousVersion);

            // Hex literals for Guid.Empty, ChannelDriveType, and FeedDrive.Alias
            // These use .NET Guid.ToByteArray() byte ordering (little-endian for first 3 components)
            string emptyGuid, channelDriveType, feedDriveAlias;
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
                emptyGuid      = "'\\x00000000000000000000000000000000'";
                channelDriveType = "'\\x1687448F4CE3F9ED014145E043CA6612'"; // 8f448716-e34c-edf9-0141-45e043ca6612
                feedDriveAlias = "'\\x2294B44DADEBE9029AB96E9C477D1E08'"; // 4db49422-ebad-02e9-9ab9-6e9c477d1e08
            }
            else
            {
                emptyGuid      = "X'00000000000000000000000000000000'";
                channelDriveType = "X'1687448F4CE3F9ED014145E043CA6612'"; // 8f448716-e34c-edf9-0141-45e043ca6612
                feedDriveAlias = "X'2294B44DADEBE9029AB96E9C477D1E08'"; // 4db49422-ebad-02e9-9ab9-6e9c477d1e08
            }

            await using var copyCommand = cn.CreateCommand();
            {
                // Old schema: (rowId, identityId, identity, driveId, created, modified)
                // New schema: (rowId, identityId, subscriberOdinId, sourceDriveId, sourceDriveTypeId,
                //              subscriberTargetDriveId, subscriptionKind, lastNotification, lastQuery, created, modified)
                // driveId = Guid.Empty  → AllNotifications: sourceDriveTypeId=ChannelDriveType, sourceDriveId=NULL
                // driveId != Guid.Empty → SelectedChannels: sourceDriveId=driveId, sourceDriveTypeId=NULL
                copyCommand.CommandText =
                    "INSERT INTO FollowsMeMigrationsV202603141230 " +
                    "(rowId,identityId,subscriberOdinId,sourceDriveId,sourceDriveTypeId,subscriberTargetDriveId,subscriptionKind,lastNotification,lastQuery,created,modified) " +
                    "SELECT rowId, identityId, identity, " +
                    $"CASE WHEN driveId = {emptyGuid} THEN NULL ELSE driveId END, " +
                    $"CASE WHEN driveId = {emptyGuid} THEN {channelDriveType} ELSE NULL END, " +
                    $"{feedDriveAlias}, " +
                    $"CASE WHEN driveId = {emptyGuid} THEN 1 ELSE 2 END, " +
                    "0, 0, created, modified " +
                    "FROM FollowsMe;";
                return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202603141230
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "FollowsMe", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "FollowsMeMigrationsV202603141230", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "FollowsMe", "FollowsMeMigrationsV202603141230") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "FollowsMe", $"FollowsMeMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "FollowsMeMigrationsV202603141230", "FollowsMe");
                    await CheckSqlTableVersion(cn, "FollowsMe", MigrationVersion);
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
            await CheckSqlTableVersion(cn, "FollowsMe", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"FollowsMeMigrationsV{PreviousVersion}", "FollowsMe") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "FollowsMe", "FollowsMeMigrationsV202603141230");
                    await SqlHelper.RenameAsync(cn, $"FollowsMeMigrationsV{PreviousVersion}", "FollowsMe");
                    await CheckSqlTableVersion(cn, "FollowsMe", PreviousVersion);
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
