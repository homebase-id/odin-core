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
        public Guid DriveAlias { get; set; }
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
            DriveAlias.AssertGuidNotEmpty("Guid parameter DriveAlias cannot be set to Empty GUID.");
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

        protected TableDrivesCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        protected virtual async Task<int> InsertAsync(DrivesRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Drives (identityId,DriveId,DriveAlias,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified) " +
                                           $"VALUES (@identityId,@DriveId,@DriveAlias,@TempOriginalDriveId,@DriveType,@DriveName,@MasterKeyEncryptedStorageKeyJson,@EncryptedIdIv64,@EncryptedIdValue64,@detailsJson,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@DriveId", DbType.Binary, item.DriveId);
                insertCommand.AddParameter("@DriveAlias", DbType.Binary, item.DriveAlias);
                insertCommand.AddParameter("@TempOriginalDriveId", DbType.Binary, item.TempOriginalDriveId);
                insertCommand.AddParameter("@DriveType", DbType.Binary, item.DriveType);
                insertCommand.AddParameter("@DriveName", DbType.String, item.DriveName);
                insertCommand.AddParameter("@MasterKeyEncryptedStorageKeyJson", DbType.String, item.MasterKeyEncryptedStorageKeyJson);
                insertCommand.AddParameter("@EncryptedIdIv64", DbType.String, item.EncryptedIdIv64);
                insertCommand.AddParameter("@EncryptedIdValue64", DbType.String, item.EncryptedIdValue64);
                insertCommand.AddParameter("@detailsJson", DbType.String, item.detailsJson);
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
                insertCommand.CommandText = "INSERT INTO Drives (identityId,DriveId,DriveAlias,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified) " +
                                            $"VALUES (@identityId,@DriveId,@DriveAlias,@TempOriginalDriveId,@DriveType,@DriveName,@MasterKeyEncryptedStorageKeyJson,@EncryptedIdIv64,@EncryptedIdValue64,@detailsJson,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@DriveId", DbType.Binary, item.DriveId);
                insertCommand.AddParameter("@DriveAlias", DbType.Binary, item.DriveAlias);
                insertCommand.AddParameter("@TempOriginalDriveId", DbType.Binary, item.TempOriginalDriveId);
                insertCommand.AddParameter("@DriveType", DbType.Binary, item.DriveType);
                insertCommand.AddParameter("@DriveName", DbType.String, item.DriveName);
                insertCommand.AddParameter("@MasterKeyEncryptedStorageKeyJson", DbType.String, item.MasterKeyEncryptedStorageKeyJson);
                insertCommand.AddParameter("@EncryptedIdIv64", DbType.String, item.EncryptedIdIv64);
                insertCommand.AddParameter("@EncryptedIdValue64", DbType.String, item.EncryptedIdValue64);
                insertCommand.AddParameter("@detailsJson", DbType.String, item.detailsJson);
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
                upsertCommand.CommandText = "INSERT INTO Drives (identityId,DriveId,DriveAlias,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified) " +
                                            $"VALUES (@identityId,@DriveId,@DriveAlias,@TempOriginalDriveId,@DriveType,@DriveName,@MasterKeyEncryptedStorageKeyJson,@EncryptedIdIv64,@EncryptedIdValue64,@detailsJson,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId,DriveId) DO UPDATE "+
                                            $"SET DriveAlias = @DriveAlias,TempOriginalDriveId = @TempOriginalDriveId,DriveType = @DriveType,DriveName = @DriveName,MasterKeyEncryptedStorageKeyJson = @MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64 = @EncryptedIdIv64,EncryptedIdValue64 = @EncryptedIdValue64,detailsJson = @detailsJson,modified = {upsertCommand.SqlMax()}(Drives.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@DriveId", DbType.Binary, item.DriveId);
                upsertCommand.AddParameter("@DriveAlias", DbType.Binary, item.DriveAlias);
                upsertCommand.AddParameter("@TempOriginalDriveId", DbType.Binary, item.TempOriginalDriveId);
                upsertCommand.AddParameter("@DriveType", DbType.Binary, item.DriveType);
                upsertCommand.AddParameter("@DriveName", DbType.String, item.DriveName);
                upsertCommand.AddParameter("@MasterKeyEncryptedStorageKeyJson", DbType.String, item.MasterKeyEncryptedStorageKeyJson);
                upsertCommand.AddParameter("@EncryptedIdIv64", DbType.String, item.EncryptedIdIv64);
                upsertCommand.AddParameter("@EncryptedIdValue64", DbType.String, item.EncryptedIdValue64);
                upsertCommand.AddParameter("@detailsJson", DbType.String, item.detailsJson);
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
                                            $"SET DriveAlias = @DriveAlias,TempOriginalDriveId = @TempOriginalDriveId,DriveType = @DriveType,DriveName = @DriveName,MasterKeyEncryptedStorageKeyJson = @MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64 = @EncryptedIdIv64,EncryptedIdValue64 = @EncryptedIdValue64,detailsJson = @detailsJson,modified = {updateCommand.SqlMax()}(Drives.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId AND DriveId = @DriveId) "+
                                            "RETURNING created,modified,rowId;";
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@DriveId", DbType.Binary, item.DriveId);
                updateCommand.AddParameter("@DriveAlias", DbType.Binary, item.DriveAlias);
                updateCommand.AddParameter("@TempOriginalDriveId", DbType.Binary, item.TempOriginalDriveId);
                updateCommand.AddParameter("@DriveType", DbType.Binary, item.DriveType);
                updateCommand.AddParameter("@DriveName", DbType.String, item.DriveName);
                updateCommand.AddParameter("@MasterKeyEncryptedStorageKeyJson", DbType.String, item.MasterKeyEncryptedStorageKeyJson);
                updateCommand.AddParameter("@EncryptedIdIv64", DbType.String, item.EncryptedIdIv64);
                updateCommand.AddParameter("@EncryptedIdValue64", DbType.String, item.EncryptedIdValue64);
                updateCommand.AddParameter("@detailsJson", DbType.String, item.detailsJson);
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
            sl.Add("DriveAlias");
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

        // SELECT rowId,identityId,DriveId,DriveAlias,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified
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
            item.DriveAlias = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.TempOriginalDriveId = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[4]);
            item.DriveType = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[5]);
            item.DriveName = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.MasterKeyEncryptedStorageKeyJson = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[7];
            item.EncryptedIdIv64 = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[8];
            item.EncryptedIdValue64 = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[9];
            item.detailsJson = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[10];
            item.created = (rdr[11] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[11]);
            item.modified = (rdr[12] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[12]);
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid DriveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM Drives " +
                                             "WHERE identityId = @identityId AND DriveId = @DriveId";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@DriveId", DbType.Binary, DriveId);
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
                                             "RETURNING rowId,DriveAlias,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@DriveId", DbType.Binary, DriveId);
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
            item.DriveAlias = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.TempOriginalDriveId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.DriveType = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.DriveName = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.MasterKeyEncryptedStorageKeyJson = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.EncryptedIdIv64 = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.EncryptedIdValue64 = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[7];
            item.detailsJson = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[8];
            item.created = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[9]);
            item.modified = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[10]);
            return item;
       }

        protected virtual async Task<DrivesRecord> GetAsync(Guid identityId,Guid DriveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,DriveAlias,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM Drives " +
                                             "WHERE identityId = @identityId AND DriveId = @DriveId LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@DriveId", DbType.Binary, DriveId);
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
            item.DriveAlias = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.TempOriginalDriveId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.DriveType = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.DriveName = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.MasterKeyEncryptedStorageKeyJson = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.EncryptedIdIv64 = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.EncryptedIdValue64 = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[7];
            item.detailsJson = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[8];
            item.created = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[9]);
            item.modified = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[10]);
            return item;
       }

        protected virtual async Task<DrivesRecord> GetByDriveIdAsync(Guid identityId,Guid DriveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,DriveAlias,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM Drives " +
                                             "WHERE identityId = @identityId AND DriveId = @DriveId LIMIT 1 "+
                                             ";";

                get1Command.AddParameter("@identityId", DbType.Binary, identityId);
                get1Command.AddParameter("@DriveId", DbType.Binary, DriveId);
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
            item.DriveAlias = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.TempOriginalDriveId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.DriveName = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.MasterKeyEncryptedStorageKeyJson = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.EncryptedIdIv64 = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.EncryptedIdValue64 = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[7];
            item.detailsJson = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[8];
            item.created = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[9]);
            item.modified = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[10]);
            return item;
       }

        protected virtual async Task<List<DrivesRecord>> GetByDriveTypeAsync(Guid identityId,Guid DriveType)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT rowId,DriveId,DriveAlias,TempOriginalDriveId,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM Drives " +
                                             "WHERE identityId = @identityId AND DriveType = @DriveType "+
                                             ";";

                get2Command.AddParameter("@identityId", DbType.Binary, identityId);
                get2Command.AddParameter("@DriveType", DbType.Binary, DriveType);
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

        protected DrivesRecord ReadRecordFromReader3(DbDataReader rdr,Guid identityId,Guid DriveAlias,Guid DriveType)
        {
            var result = new List<DrivesRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DrivesRecord();
            item.identityId = identityId;
            item.DriveAlias = DriveAlias;
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
            item.modified = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[9]);
            return item;
       }

        protected virtual async Task<DrivesRecord> GetByTargetDriveAsync(Guid identityId,Guid DriveAlias,Guid DriveType)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get3Command = cn.CreateCommand();
            {
                get3Command.CommandText = "SELECT rowId,DriveId,TempOriginalDriveId,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM Drives " +
                                             "WHERE identityId = @identityId AND DriveAlias = @DriveAlias AND DriveType = @DriveType LIMIT 1 "+
                                             ";";

                get3Command.AddParameter("@identityId", DbType.Binary, identityId);
                get3Command.AddParameter("@DriveAlias", DbType.Binary, DriveAlias);
                get3Command.AddParameter("@DriveType", DbType.Binary, DriveType);
                {
                    using (var rdr = await get3Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader3(rdr,identityId,DriveAlias,DriveType);
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
            await using var getPaging11Command = cn.CreateCommand();
            {
                getPaging11Command.CommandText = "SELECT rowId,identityId,DriveId,DriveAlias,TempOriginalDriveId,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM Drives " +
                                            "WHERE (identityId = @identityId) AND created <= @created AND rowId < @rowId ORDER BY created DESC , rowId DESC LIMIT @count;";

                getPaging11Command.AddParameter("@created", DbType.Int64, inCursor?.milliseconds);
                getPaging11Command.AddParameter("@rowId", DbType.Int64, rowid);
                getPaging11Command.AddParameter("@count", DbType.Int64, count+1);
                getPaging11Command.AddParameter("@identityId", DbType.Binary, identityId);

                {
                    await using (var rdr = await getPaging11Command.ExecuteReaderAsync(CommandBehavior.Default))
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
