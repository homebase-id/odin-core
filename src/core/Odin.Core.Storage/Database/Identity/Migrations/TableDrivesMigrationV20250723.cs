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
    public class TableDrivesMigrationV20250723 : MigrationBase
    {
        public override int MigrationVersion => 20250723;
        public TableDrivesMigrationV20250723(MigrationListBase container) : base(container)
        {
        }

        public override async Task EnsureTableExistsAsync(IConnectionWrapper cn, bool dropExisting = false)
        {
            if (dropExisting)
                await MigrationBase.DeleteTableAsync(cn, "DrivesMigrationsV20250723");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DrivesMigrationsV20250723 IS '{ \"Version\": 20250723 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS DrivesMigrationsV20250723( -- { \"Version\": 20250723 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"DriveId BYTEA NOT NULL, "
                   +"StorageKeyCheckValue BYTEA NOT NULL, "
                   +"DriveType BYTEA NOT NULL, "
                   +"DriveName TEXT NOT NULL, "
                   +"MasterKeyEncryptedStorageKeyJson TEXT NOT NULL, "
                   +"EncryptedIdIv64 TEXT NOT NULL, "
                   +"EncryptedIdValue64 TEXT NOT NULL, "
                   +"detailsJson TEXT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,DriveId)"
                   +", UNIQUE(identityId,DriveId,DriveType)"
                   +$"){wori};"
                   ;
            await MigrationBase.CreateTableAsync(cn, createSql, commentSql);
        }

        public static List<string> GetColumnNames()
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
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        public async Task<int> CopyDataAsync(IConnectionWrapper cn)
        {
            await using var copyCommand = cn.CreateCommand();
            {
                copyCommand.CommandText = "INSERT INTO DrivesMigrationsV20250723 (rowId,identityId,DriveId,StorageKeyCheckValue,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified) " +
               $"SELECT rowId,identityId,DriveId,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified "+
               $"FROM Drives;";
               return await copyCommand.ExecuteNonQueryAsync();
            }
        }

        // DriveMainIndex is presumed to be the previous version
        // Will upgrade from the previous version to version 20250723
        public override async Task UpAsync(IConnectionWrapper cn)
        {
            Container.ValidateMigrationList();
            await CheckSqlTableVersion(cn, "Drives", Container.PreviousVersionInt(this));
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    await EnsureTableExistsAsync(cn, dropExisting: true);
                    if (await CopyDataAsync(cn) < 0)
                        throw new MigrationException("Unable to copy the data");
                    if (await VerifyRowCount(cn, "Drives", "DrivesMigrationsV20250723") == false)
                        throw new MigrationException("Mismatching row counts");
                    await RenameAsync(cn, "Drives", $"DrivesMigrationsV{Container.PreviousVersionInt(this)}");
                    await RenameAsync(cn, "DrivesMigrationsV20250723", "Drives");
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
            Container.ValidateMigrationList();
            await CheckSqlTableVersion(cn, "Drives", this.MigrationVersion);
            try
            {
                using (var trn = await cn.BeginStackedTransactionAsync())
                {
                    if (await VerifyRowCount(cn, $"DrivesMigrationsV{Container.PreviousVersionInt(this)}", "Drives") == false)
                        throw new MigrationException("Mismatching row counts - bad idea to downgrade");
                    await RenameAsync(cn, "Drives", "DrivesMigrationsV20250723");
                    await RenameAsync(cn, $"DrivesMigrationsV{Container.PreviousVersionInt(this)}", "Drives");
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
