using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Attestation.Connection;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.KeyChain.Connection;
using Odin.Core.Storage.Database.Notary.Connection;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage;
using Odin.Core.Util;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.SQLite; //added for homebase social sync

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record DrivesRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid DriveId { get; set; }
        public Guid TempOriginalDriveId { get; set; }
        public Guid DriveType { get; set; }
        public string DriveName { get; set; }
        public string MasterKeyEncryptedStorageKeyJson { get; set; }
        public string EncryptedIdIv64 { get; set; }
        public string EncryptedIdValue64 { get; set; }
        public string detailsJson { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            DriveId.AssertGuidNotEmpty("Guid parameter DriveId cannot be set to Empty GUID.");
            TempOriginalDriveId.AssertGuidNotEmpty("Guid parameter TempOriginalDriveId cannot be set to Empty GUID.");
            DriveType.AssertGuidNotEmpty("Guid parameter DriveType cannot be set to Empty GUID.");
            if (DriveName == null) throw new OdinDatabaseValidationException("Cannot be null DriveName");
            if (DriveName?.Length < 0) throw new OdinDatabaseValidationException($"Too short DriveName, was {DriveName.Length} (min 0)");
            if (DriveName?.Length > 1024) throw new OdinDatabaseValidationException($"Too long DriveName, was {DriveName.Length} (max 1024)");
            if (MasterKeyEncryptedStorageKeyJson == null) throw new OdinDatabaseValidationException("Cannot be null MasterKeyEncryptedStorageKeyJson");
            if (MasterKeyEncryptedStorageKeyJson?.Length < 0) throw new OdinDatabaseValidationException($"Too short MasterKeyEncryptedStorageKeyJson, was {MasterKeyEncryptedStorageKeyJson.Length} (min 0)");
            if (MasterKeyEncryptedStorageKeyJson?.Length > 1024) throw new OdinDatabaseValidationException($"Too long MasterKeyEncryptedStorageKeyJson, was {MasterKeyEncryptedStorageKeyJson.Length} (max 1024)");
            if (EncryptedIdIv64 == null) throw new OdinDatabaseValidationException("Cannot be null EncryptedIdIv64");
            if (EncryptedIdIv64?.Length < 0) throw new OdinDatabaseValidationException($"Too short EncryptedIdIv64, was {EncryptedIdIv64.Length} (min 0)");
            if (EncryptedIdIv64?.Length > 1024) throw new OdinDatabaseValidationException($"Too long EncryptedIdIv64, was {EncryptedIdIv64.Length} (max 1024)");
            if (EncryptedIdValue64 == null) throw new OdinDatabaseValidationException("Cannot be null EncryptedIdValue64");
            if (EncryptedIdValue64?.Length < 0) throw new OdinDatabaseValidationException($"Too short EncryptedIdValue64, was {EncryptedIdValue64.Length} (min 0)");
            if (EncryptedIdValue64?.Length > 1024) throw new OdinDatabaseValidationException($"Too long EncryptedIdValue64, was {EncryptedIdValue64.Length} (max 1024)");
            if (detailsJson == null) throw new OdinDatabaseValidationException("Cannot be null detailsJson");
            if (detailsJson?.Length < 0) throw new OdinDatabaseValidationException($"Too short detailsJson, was {detailsJson.Length} (min 0)");
            if (detailsJson?.Length > 21504) throw new OdinDatabaseValidationException($"Too long detailsJson, was {detailsJson.Length} (max 21504)");
        }
    } // End of record DrivesRecord

    public abstract class TableDrivesCRUD : TableBase
    {
        private ScopedIdentityConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "Drives";

        protected TableDrivesCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "Drives");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE Drives IS '{ \"Version\": 202507221210 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS Drives( -- { \"Version\": 202507221210 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"DriveId BYTEA NOT NULL, "
                   +"TempOriginalDriveId BYTEA NOT NULL, "
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
            await SqlHelper.CreateTableWithCommentAsync(cn, "Drives", createSql, commentSql);
        }

        protected virtual async Task<int> InsertAsync(DrivesRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Drives (identityId,DriveId,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified) " +
                                           $"VALUES (@identityId,@DriveId,@TempOriginalDriveId,@DriveType,@DriveName,@MasterKeyEncryptedStorageKeyJson,@EncryptedIdIv64,@EncryptedIdValue64,@detailsJson,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@DriveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@TempOriginalDriveId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Binary;
                insertParam4.ParameterName = "@DriveType";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.String;
                insertParam5.ParameterName = "@DriveName";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.String;
                insertParam6.ParameterName = "@MasterKeyEncryptedStorageKeyJson";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.String;
                insertParam7.ParameterName = "@EncryptedIdIv64";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.String;
                insertParam8.ParameterName = "@EncryptedIdValue64";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.DbType = DbType.String;
                insertParam9.ParameterName = "@detailsJson";
                insertCommand.Parameters.Add(insertParam9);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.DriveId.ToByteArray();
                insertParam3.Value = item.TempOriginalDriveId.ToByteArray();
                insertParam4.Value = item.DriveType.ToByteArray();
                insertParam5.Value = item.DriveName;
                insertParam6.Value = item.MasterKeyEncryptedStorageKeyJson;
                insertParam7.Value = item.EncryptedIdIv64;
                insertParam8.Value = item.EncryptedIdValue64;
                insertParam9.Value = item.detailsJson;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(DrivesRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Drives (identityId,DriveId,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified) " +
                                            $"VALUES (@identityId,@DriveId,@TempOriginalDriveId,@DriveType,@DriveName,@MasterKeyEncryptedStorageKeyJson,@EncryptedIdIv64,@EncryptedIdValue64,@detailsJson,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@DriveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@TempOriginalDriveId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Binary;
                insertParam4.ParameterName = "@DriveType";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.String;
                insertParam5.ParameterName = "@DriveName";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.String;
                insertParam6.ParameterName = "@MasterKeyEncryptedStorageKeyJson";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.String;
                insertParam7.ParameterName = "@EncryptedIdIv64";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.String;
                insertParam8.ParameterName = "@EncryptedIdValue64";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.DbType = DbType.String;
                insertParam9.ParameterName = "@detailsJson";
                insertCommand.Parameters.Add(insertParam9);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.DriveId.ToByteArray();
                insertParam3.Value = item.TempOriginalDriveId.ToByteArray();
                insertParam4.Value = item.DriveType.ToByteArray();
                insertParam5.Value = item.DriveName;
                insertParam6.Value = item.MasterKeyEncryptedStorageKeyJson;
                insertParam7.Value = item.EncryptedIdIv64;
                insertParam8.Value = item.EncryptedIdValue64;
                insertParam9.Value = item.detailsJson;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(DrivesRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO Drives (identityId,DriveId,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified) " +
                                            $"VALUES (@identityId,@DriveId,@TempOriginalDriveId,@DriveType,@DriveName,@MasterKeyEncryptedStorageKeyJson,@EncryptedIdIv64,@EncryptedIdValue64,@detailsJson,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId,DriveId) DO UPDATE "+
                                            $"SET TempOriginalDriveId = @TempOriginalDriveId,DriveType = @DriveType,DriveName = @DriveName,MasterKeyEncryptedStorageKeyJson = @MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64 = @EncryptedIdIv64,EncryptedIdValue64 = @EncryptedIdValue64,detailsJson = @detailsJson,modified = {upsertCommand.SqlMax()}(Drives.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.Binary;
                upsertParam2.ParameterName = "@DriveId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.Binary;
                upsertParam3.ParameterName = "@TempOriginalDriveId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.Binary;
                upsertParam4.ParameterName = "@DriveType";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.DbType = DbType.String;
                upsertParam5.ParameterName = "@DriveName";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.DbType = DbType.String;
                upsertParam6.ParameterName = "@MasterKeyEncryptedStorageKeyJson";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.DbType = DbType.String;
                upsertParam7.ParameterName = "@EncryptedIdIv64";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.DbType = DbType.String;
                upsertParam8.ParameterName = "@EncryptedIdValue64";
                upsertCommand.Parameters.Add(upsertParam8);
                var upsertParam9 = upsertCommand.CreateParameter();
                upsertParam9.DbType = DbType.String;
                upsertParam9.ParameterName = "@detailsJson";
                upsertCommand.Parameters.Add(upsertParam9);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.DriveId.ToByteArray();
                upsertParam3.Value = item.TempOriginalDriveId.ToByteArray();
                upsertParam4.Value = item.DriveType.ToByteArray();
                upsertParam5.Value = item.DriveName;
                upsertParam6.Value = item.MasterKeyEncryptedStorageKeyJson;
                upsertParam7.Value = item.EncryptedIdIv64;
                upsertParam8.Value = item.EncryptedIdValue64;
                upsertParam9.Value = item.detailsJson;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(DrivesRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE Drives " +
                                            $"SET TempOriginalDriveId = @TempOriginalDriveId,DriveType = @DriveType,DriveName = @DriveName,MasterKeyEncryptedStorageKeyJson = @MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64 = @EncryptedIdIv64,EncryptedIdValue64 = @EncryptedIdValue64,detailsJson = @detailsJson,modified = {updateCommand.SqlMax()}(Drives.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId AND DriveId = @DriveId) "+
                                            "RETURNING created,modified,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.Binary;
                updateParam2.ParameterName = "@DriveId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.Binary;
                updateParam3.ParameterName = "@TempOriginalDriveId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.Binary;
                updateParam4.ParameterName = "@DriveType";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.DbType = DbType.String;
                updateParam5.ParameterName = "@DriveName";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.DbType = DbType.String;
                updateParam6.ParameterName = "@MasterKeyEncryptedStorageKeyJson";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.DbType = DbType.String;
                updateParam7.ParameterName = "@EncryptedIdIv64";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.DbType = DbType.String;
                updateParam8.ParameterName = "@EncryptedIdValue64";
                updateCommand.Parameters.Add(updateParam8);
                var updateParam9 = updateCommand.CreateParameter();
                updateParam9.DbType = DbType.String;
                updateParam9.ParameterName = "@detailsJson";
                updateCommand.Parameters.Add(updateParam9);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.DriveId.ToByteArray();
                updateParam3.Value = item.TempOriginalDriveId.ToByteArray();
                updateParam4.Value = item.DriveType.ToByteArray();
                updateParam5.Value = item.DriveName;
                updateParam6.Value = item.MasterKeyEncryptedStorageKeyJson;
                updateParam7.Value = item.EncryptedIdIv64;
                updateParam8.Value = item.EncryptedIdValue64;
                updateParam9.Value = item.detailsJson;
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected new async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM Drives;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("DriveId");
            sl.Add("TempOriginalDriveId");
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

        // SELECT rowId,identityId,DriveId,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified
        protected DrivesRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<DrivesRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DrivesRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.DriveId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.TempOriginalDriveId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.DriveType = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[4]);
            item.DriveName = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.MasterKeyEncryptedStorageKeyJson = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.EncryptedIdIv64 = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[7];
            item.EncryptedIdValue64 = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[8];
            item.detailsJson = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[9];
            item.created = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[10]);
            item.modified = (rdr[11] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[11]); // HACK
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid DriveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM Drives " +
                                             "WHERE identityId = @identityId AND DriveId = @DriveId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.DbType = DbType.Binary;
                delete0Param2.ParameterName = "@DriveId";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = DriveId.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<DrivesRecord> PopAsync(Guid identityId,Guid DriveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM Drives " +
                                             "WHERE identityId = @identityId AND DriveId = @DriveId " + 
                                             "RETURNING rowId,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified";
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.Binary;
                deleteParam1.ParameterName = "@identityId";
                deleteCommand.Parameters.Add(deleteParam1);
                var deleteParam2 = deleteCommand.CreateParameter();
                deleteParam2.DbType = DbType.Binary;
                deleteParam2.ParameterName = "@DriveId";
                deleteCommand.Parameters.Add(deleteParam2);

                deleteParam1.Value = identityId.ToByteArray();
                deleteParam2.Value = DriveId.ToByteArray();
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,DriveId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected DrivesRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid DriveId)
        {
            var result = new List<DrivesRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DrivesRecord();
            item.identityId = identityId;
            item.DriveId = DriveId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.TempOriginalDriveId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.DriveType = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.DriveName = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.MasterKeyEncryptedStorageKeyJson = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.EncryptedIdIv64 = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.EncryptedIdValue64 = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.detailsJson = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[7];
            item.created = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            item.modified = (rdr[9] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[9]); // HACK
            return item;
       }

        protected virtual async Task<DrivesRecord> GetAsync(Guid identityId,Guid DriveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM Drives " +
                                             "WHERE identityId = @identityId AND DriveId = @DriveId LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.DbType = DbType.Binary;
                get0Param2.ParameterName = "@DriveId";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = DriveId.ToByteArray();
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,DriveId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected DrivesRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId,Guid DriveId)
        {
            var result = new List<DrivesRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DrivesRecord();
            item.identityId = identityId;
            item.DriveId = DriveId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.TempOriginalDriveId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.DriveType = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.DriveName = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.MasterKeyEncryptedStorageKeyJson = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.EncryptedIdIv64 = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.EncryptedIdValue64 = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.detailsJson = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[7];
            item.created = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            item.modified = (rdr[9] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[9]); // HACK
            return item;
       }

        protected virtual async Task<DrivesRecord> GetByDriveIdAsync(Guid identityId,Guid DriveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM Drives " +
                                             "WHERE identityId = @identityId AND DriveId = @DriveId LIMIT 1;"+
                                             ";";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.DbType = DbType.Binary;
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.DbType = DbType.Binary;
                get1Param2.ParameterName = "@DriveId";
                get1Command.Parameters.Add(get1Param2);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = DriveId.ToByteArray();
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader1(rdr,identityId,DriveId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected DrivesRecord ReadRecordFromReader2(DbDataReader rdr,Guid identityId,Guid DriveType)
        {
            var result = new List<DrivesRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DrivesRecord();
            item.identityId = identityId;
            item.DriveType = DriveType;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.DriveId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.TempOriginalDriveId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.DriveName = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.MasterKeyEncryptedStorageKeyJson = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.EncryptedIdIv64 = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.EncryptedIdValue64 = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.detailsJson = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[7];
            item.created = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            item.modified = (rdr[9] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[9]); // HACK
            return item;
       }

        protected virtual async Task<List<DrivesRecord>> GetByDriveTypeAsync(Guid identityId,Guid DriveType)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT rowId,DriveId,TempOriginalDriveId,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM Drives " +
                                             "WHERE identityId = @identityId AND DriveType = @DriveType;"+
                                             ";";
                var get2Param1 = get2Command.CreateParameter();
                get2Param1.DbType = DbType.Binary;
                get2Param1.ParameterName = "@identityId";
                get2Command.Parameters.Add(get2Param1);
                var get2Param2 = get2Command.CreateParameter();
                get2Param2.DbType = DbType.Binary;
                get2Param2.ParameterName = "@DriveType";
                get2Command.Parameters.Add(get2Param2);

                get2Param1.Value = identityId.ToByteArray();
                get2Param2.Value = DriveType.ToByteArray();
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<DrivesRecord>();
                        }
                        var result = new List<DrivesRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader2(rdr,identityId,DriveType));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected DrivesRecord ReadRecordFromReader3(DbDataReader rdr,Guid identityId,Guid DriveId,Guid DriveType)
        {
            var result = new List<DrivesRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DrivesRecord();
            item.identityId = identityId;
            item.DriveId = DriveId;
            item.DriveType = DriveType;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.TempOriginalDriveId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.DriveName = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.MasterKeyEncryptedStorageKeyJson = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.EncryptedIdIv64 = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.EncryptedIdValue64 = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.detailsJson = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.created = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            item.modified = (rdr[8] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[8]); // HACK
            return item;
       }

        protected virtual async Task<DrivesRecord> GetByTargetDriveAsync(Guid identityId,Guid DriveId,Guid DriveType)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get3Command = cn.CreateCommand();
            {
                get3Command.CommandText = "SELECT rowId,TempOriginalDriveId,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM Drives " +
                                             "WHERE identityId = @identityId AND DriveId = @DriveId AND DriveType = @DriveType LIMIT 1;"+
                                             ";";
                var get3Param1 = get3Command.CreateParameter();
                get3Param1.DbType = DbType.Binary;
                get3Param1.ParameterName = "@identityId";
                get3Command.Parameters.Add(get3Param1);
                var get3Param2 = get3Command.CreateParameter();
                get3Param2.DbType = DbType.Binary;
                get3Param2.ParameterName = "@DriveId";
                get3Command.Parameters.Add(get3Param2);
                var get3Param3 = get3Command.CreateParameter();
                get3Param3.DbType = DbType.Binary;
                get3Param3.ParameterName = "@DriveType";
                get3Command.Parameters.Add(get3Param3);

                get3Param1.Value = identityId.ToByteArray();
                get3Param2.Value = DriveId.ToByteArray();
                get3Param3.Value = DriveType.ToByteArray();
                {
                    using (var rdr = await get3Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader3(rdr,identityId,DriveId,DriveType);
                        return r;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<DrivesRecord>, UnixTimeUtc? nextCursor, long nextRowId)> PagingByCreatedAsync(int count, Guid identityId, UnixTimeUtc? inCursor, long? rowid)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (count == int.MaxValue)
                count--; // avoid overflow when doing +1 on the param below
            if (inCursor == null)
                inCursor = new UnixTimeUtc(long.MaxValue);
            if (rowid == null)
                rowid = long.MaxValue;

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging10Command = cn.CreateCommand();
            {
                getPaging10Command.CommandText = "SELECT rowId,identityId,DriveId,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM Drives " +
                                            "WHERE (identityId = @identityId) AND created <= @created AND rowId < @rowId ORDER BY created DESC , rowId DESC LIMIT @count;";
                var getPaging10Param1 = getPaging10Command.CreateParameter();
                getPaging10Param1.DbType = DbType.Int64;
                getPaging10Param1.ParameterName = "@created";
                getPaging10Command.Parameters.Add(getPaging10Param1);
                var getPaging10Param2 = getPaging10Command.CreateParameter();
                getPaging10Param2.DbType = DbType.Int64;
                getPaging10Param2.ParameterName = "@rowId";
                getPaging10Command.Parameters.Add(getPaging10Param2);
                var getPaging10Param3 = getPaging10Command.CreateParameter();
                getPaging10Param3.DbType = DbType.Int64;
                getPaging10Param3.ParameterName = "@count";
                getPaging10Command.Parameters.Add(getPaging10Param3);
                var getPaging10Param4 = getPaging10Command.CreateParameter();
                getPaging10Param4.DbType = DbType.Binary;
                getPaging10Param4.ParameterName = "@identityId";
                getPaging10Command.Parameters.Add(getPaging10Param4);

                getPaging10Param1.Value = inCursor?.milliseconds;
                getPaging10Param2.Value = rowid;
                getPaging10Param3.Value = count+1;
                getPaging10Param4.Value = identityId.ToByteArray();

                {
                    await using (var rdr = await getPaging10Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<DrivesRecord>();
                        UnixTimeUtc? nextCursor;
                        long nextRowId;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].created;
                                nextRowId = result[n - 1].rowId;
                        }
                        else
                        {
                            nextCursor = null;
                            nextRowId = 0;
                        }
                        return (result, nextCursor, nextRowId);
                    } // using
                } //
            } // using 
        } // PagingGet

    }
}
