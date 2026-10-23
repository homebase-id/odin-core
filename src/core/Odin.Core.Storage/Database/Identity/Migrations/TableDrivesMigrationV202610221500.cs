using System;
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
                // Collaborative (the column default, today's behaviour) with no zero-knowledge key.
                // FillSharingModelAsync then labels the built-in drives.
                copyCommand.CommandText = "INSERT INTO DrivesMigrationsV202610221500 (rowId,identityId,DriveId,StorageKeyCheckValue,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,AppId,DriveSlug,DriveTypeSlug,WriteOnlyKeyPair,created,modified) " +
               $"SELECT rowId,identityId,DriveId,StorageKeyCheckValue,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,AppId,DriveSlug,DriveTypeSlug,WriteOnlyKeyPair,created,modified "+
               $"FROM Drives;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Labelling the built-in drives (docs/zero-knowledge-drives.md, "Migration")
        //
        // A FROZEN copy of the migration table in that doc, keyed by drive id (a drive's id is its
        // alias).  The ids live in Odin.Services, which this project cannot reference, and a migration
        // must not change behaviour when they do.
        //
        // The label alone changes nothing: a drive is zero knowledge only once the browser converts it.
        // Collaborative is the column default, so Community, Public Posts and every other drive need no
        // row here.  Values match Odin.Services.Drives.DriveSharingModel.
        // ---------------------------------------------------------------------------------------------
        //
        // A test holds this map and BuiltinDrives together (DriveSharingModelMapTests).

        private const int Private = 1;
        private const int SelectiveShare = 2;
        private const int Unclassified = 3;

        internal static readonly IReadOnlyDictionary<Guid, int> BuiltinSharingModels = new Dictionary<Guid, int>
        {
            [Guid.Parse("2612429d1c3f037282b8d42fb2cc0499")] = Private, // contacts
            [Guid.Parse("92bbcad8-3558-417b-9376-9976c086a674")] = Private, // email
            [Guid.Parse("4db49422ebad02e99ab96e9c477d1e08")] = Private, // feed
            [Guid.Parse("a6f991e214b11c8c9796f664e1ec0cac")] = Private, // wallet
            [Guid.Parse("f47ac10b58cc4372a5670e02b2c3d479")] = Private, // vault
            [Guid.Parse("9ff813aff2d61e2f9b9db189e72d1a11")] = Private, // chat
            [Guid.Parse("e69b5a48a663482fbfd846f3b0b143b0")] = Private, // mail
            [Guid.Parse("46242d0d67604b2aa683f05cd48d4aef")] = Private, // shard recovery
            [Guid.Parse("6483b7b1f71bd43eb6896c86148668cc")] = SelectiveShare, // photo library
            [Guid.Parse("3b9c5f2e-7a41-4d6b-9e0c-8f1a2b3c4d5e")] = SelectiveShare, // stickers
            [Guid.Parse("2e191a14-8640-4ebc-b0c8-aaac913f6fa8")] = SelectiveShare, // location
            [Guid.Parse("a44e7a2651f44a26ad125d7627b35d0e")] = SelectiveShare, // lists
            [Guid.Parse("a85f8562-6c74-4947-896b-619812cafccc")] = SelectiveShare, // moments
            [Guid.Parse("ec83345af6a747d4404ef8b0f8844caa")] = SelectiveShare, // home page config
            [Guid.Parse("8f12d8c4933813d378488d91ed23b64c")] = SelectiveShare, // profile
            [Guid.Parse("6d1711af-8b93-43ef-b798-b84d51f25828")] = SelectiveShare, // webdrop
            [Guid.Parse("90f5e74ab7f9efda0ac298373a32ad8c")] = Unclassified, // transient temp
        };

        // Runs on the new table, after the copy.  No identity filter: on Postgres every identity shares
        // the table and it is migrated once.
        private static async Task FillSharingModelAsync(IConnectionWrapper cn)
        {
            foreach (var (driveId, sharingModel) in BuiltinSharingModels)
            {
                await using var update = cn.CreateCommand();
                update.CommandText =
                    "UPDATE DrivesMigrationsV202610221500 SET SharingModel=@sharingModel WHERE DriveId=@driveId;";
                update.AddParameter("@sharingModel", DbType.Int32, sharingModel);
                update.AddParameter("@driveId", DbType.Binary, driveId);
                await update.ExecuteNonQueryAsync();
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
                    await FillSharingModelAsync(cn);
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
