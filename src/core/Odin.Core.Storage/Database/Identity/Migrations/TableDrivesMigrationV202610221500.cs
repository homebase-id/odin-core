using System;
using System.Linq;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Core.Storage;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Database.Identity.Connection;

#nullable disable

// THIS FILE WAS INITIALLY AUTO GENERATED

namespace Odin.Core.Storage.Database.Identity.Migrations
{
    public class TableDrivesMigrationV202610221500 : MigrationBase
    {
        public override Int64 MigrationVersion => 202610221500;
        public TableDrivesMigrationV202610221500(Int64 previousVersion) : base(previousVersion)
        {
        }

        public override async Task CreateTableWithCommentAsync(IConnectionWrapper cn)
        {
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DrivesMigrationsV202610221500 IS '{ \"Version\": 202610221500 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS DrivesMigrationsV202610221500( -- { \"Version\": 202610221500 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"DriveId BYTEA NOT NULL, "
                   +"StorageKeyCheckValue BYTEA NOT NULL, "
                   +"DriveType BYTEA NOT NULL, "
                   +"DriveName TEXT NOT NULL, "
                   +"MasterKeyEncryptedStorageKeyJson TEXT , "
                   +"EncryptedIdIv64 TEXT NOT NULL, "
                   +"EncryptedIdValue64 TEXT NOT NULL, "
                   +"detailsJson TEXT NOT NULL, "
                   +"AppId BYTEA NOT NULL, "
                   +"DriveSlug TEXT NOT NULL, "
                   +"DriveTypeSlug TEXT NOT NULL, "
                   +"WriteOnlyKeyPair BYTEA , "
                   +"SharingModel BIGINT NOT NULL DEFAULT 0, "
                   +"ZkMasterKeyEncryptedStorageKey TEXT , "
                   +"ZkStorageKeyEncryptedWriteOnlyPrivateKey TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,DriveId)"
                   +", UNIQUE(identityId,AppId,DriveSlug)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0DrivesMigrationsV202610221500 ON DrivesMigrationsV202610221500(identityId,DriveType);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "DrivesMigrationsV202610221500", createSql, commentSql);
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("DriveId");
            sl.Add("StorageKeyCheckValue");
            sl.Add("DriveType");
            sl.Add("DriveName");
            sl.Add("MasterKeyEncryptedStorageKeyJson");
            sl.Add("EncryptedIdIv64");
            sl.Add("EncryptedIdValue64");
            sl.Add("detailsJson");
            sl.Add("AppId");
            sl.Add("DriveSlug");
            sl.Add("DriveTypeSlug");
            sl.Add("WriteOnlyKeyPair");
            sl.Add("SharingModel");
            sl.Add("ZkMasterKeyEncryptedStorageKey");
            sl.Add("ZkStorageKeyEncryptedWriteOnlyPrivateKey");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "DrivesMigrationsV202610221500", MigrationVersion);
            await CheckSqlTableVersion(cn, "Drives", PreviousVersion);
            await using var copyCommand = cn.CreateCommand();
            {
                // SharingModel, ZkMasterKeyEncryptedStorageKey and ZkStorageKeyEncryptedWriteOnlyPrivateKey
                // are new in this version and are omitted from both lists: every existing drive lands as
                // Collaborative (the column default, today's behaviour) with no zero-knowledge key.  A
                // drive becomes Private only when the browser converts it (DriveManager.ApplyZeroKnowledgeKeysAsync).
                copyCommand.CommandText = "INSERT INTO DrivesMigrationsV202610221500 (rowId,identityId,DriveId,StorageKeyCheckValue,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,AppId,DriveSlug,DriveTypeSlug,WriteOnlyKeyPair,created,modified) " +
               $"SELECT rowId,identityId,DriveId,StorageKeyCheckValue,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,AppId,DriveSlug,DriveTypeSlug,WriteOnlyKeyPair,created,modified "+
               $"FROM Drives;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // Will upgrade from the previous version to version 202610221500
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            await CheckSqlTableVersion(cn, "Drives", PreviousVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await CreateTableWithCommentAsync(cn);
                    await CheckSqlTableVersion(cn, "DrivesMigrationsV202610221500", MigrationVersion);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "Drives", "DrivesMigrationsV202610221500") == false)
                        throw new MigrationException("Mismatching row counts");
                    await SqlHelper.RenameAsync(cn, "Drives", $"DrivesMigrationsV{PreviousVersion}");
                    await SqlHelper.RenameAsync(cn, "DrivesMigrationsV202610221500", "Drives");
                    await CheckSqlTableVersion(cn, "Drives", MigrationVersion);
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
            await CheckSqlTableVersion(cn, "Drives", MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"DrivesMigrationsV{PreviousVersion}", "Drives") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await SqlHelper.RenameAsync(cn, "Drives", "DrivesMigrationsV202610221500");
                    await SqlHelper.RenameAsync(cn, $"DrivesMigrationsV{PreviousVersion}", "Drives");
                    await CheckSqlTableVersion(cn, "Drives", PreviousVersion);
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
