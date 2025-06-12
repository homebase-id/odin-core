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

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.SQLite.AttestationDatabase
{
    public record AttestationStatusRecord
    {
        private Int64 _rowId;
        public Int64 rowId
        {
           get {
                   return _rowId;
               }
           set {
                  _rowId = value;
               }
        }
        private byte[] _attestationId;
        public byte[] attestationId
        {
           get {
                   return _attestationId;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short attestationId, was {value.Length} (min 16)");
                    if (value?.Length > 64) throw new OdinDatabaseValidationException($"Too long attestationId, was {value.Length} (max 64)");
                  _attestationId = value;
               }
        }
        internal byte[] attestationIdNoLengthCheck
        {
           get {
                   return _attestationId;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short attestationId, was {value.Length} (min 16)");
                  _attestationId = value;
               }
        }
        private Int32 _status;
        public Int32 status
        {
           get {
                   return _status;
               }
           set {
                  _status = value;
               }
        }
        private UnixTimeUtc _created;
        public UnixTimeUtc created
        {
           get {
                   return _created;
               }
           set {
                  _created = value;
               }
        }
        private UnixTimeUtc _modified;
        public UnixTimeUtc modified
        {
           get {
                   return _modified;
               }
           set {
                  _modified = value;
               }
        }
    } // End of record AttestationStatusRecord

    public class TableAttestationStatusCRUD
    {
        private readonly CacheHelper _cache;

        public TableAttestationStatusCRUD(CacheHelper cache)
        {
            _cache = cache;
        }


        public virtual async Task EnsureTableExistsAsync(DatabaseConnection conn, bool dropExisting = false)
        {
            await using var cmd = conn.db.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS AttestationStatus;";
                await conn.ExecuteNonQueryAsync(cmd);
            }
            var rowid = "";
            rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS AttestationStatus("
                   +rowid
                   +"attestationId BYTEA NOT NULL UNIQUE, "
                   +"status BIGINT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +$"){wori};"
                   ;
            await conn.ExecuteNonQueryAsync(cmd);
        }

        public virtual async Task<int> InsertAsync(DatabaseConnection conn, AttestationStatusRecord item)
        {
            using (var insertCommand = conn.db.CreateCommand())
            {
                string sqlNowStr;
                sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)"; // Needs _scopedConnectionFactory to support Postgres
                insertCommand.CommandText = "INSERT INTO AttestationStatus (attestationId,status,created,modified) " +
                                           $"VALUES (@attestationId,@status,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@attestationId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Int32;
                insertParam2.ParameterName = "@status";
                insertCommand.Parameters.Add(insertParam2);
                insertParam1.Value = item.attestationId;
                insertParam2.Value = item.status;
                await using var rdr = await conn.ExecuteReaderAsync(insertCommand, CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    if (rdr[1] == DBNull.Value)
                         item.modified = item.created;
                    else
                    {
                         long modified = (long) rdr[1];
                         item.modified = new UnixTimeUtc((long)modified);
                    }
                    item.rowId = (long) rdr[2];
                    _cache.AddOrUpdate("TableAttestationStatusCRUD", item.attestationId.ToBase64(), item);
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<bool> TryInsertAsync(DatabaseConnection conn, AttestationStatusRecord item)
        {
            using (var insertCommand = conn.db.CreateCommand())
            {
                string sqlNowStr;
                sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)"; // Needs _scopedConnectionFactory to support Postgres
                insertCommand.CommandText = "INSERT INTO AttestationStatus (attestationId,status,created,modified) " +
                                            $"VALUES (@attestationId,@status,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@attestationId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Int32;
                insertParam2.ParameterName = "@status";
                insertCommand.Parameters.Add(insertParam2);
                insertParam1.Value = item.attestationId;
                insertParam2.Value = item.status;
                await using var rdr = await conn.ExecuteReaderAsync(insertCommand, CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    if (rdr[1] == DBNull.Value)
                         item.modified = item.created;
                    else
                    {
                         long modified = (long) rdr[1];
                         item.modified = new UnixTimeUtc((long)modified);
                    }
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableAttestationStatusCRUD", item.attestationId.ToBase64(), item);
                    return true;
                }
                return false;
            }
        }

        public virtual async Task<int> UpsertAsync(DatabaseConnection conn, AttestationStatusRecord item)
        {
            using (var upsertCommand = conn.db.CreateCommand())
            {
                string sqlNowStr;
                sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)"; // Needs _scopedConnectionFactory to support Postgres
                upsertCommand.CommandText = "INSERT INTO AttestationStatus (attestationId,status,created,modified) " +
                                            $"VALUES (@attestationId,@status,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (attestationId) DO UPDATE "+
                                            $"SET status = @status,modified = MAX(AttestationStatus.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@attestationId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.Int32;
                upsertParam2.ParameterName = "@status";
                upsertCommand.Parameters.Add(upsertParam2);
                upsertParam1.Value = item.attestationId;
                upsertParam2.Value = item.status;
                await using var rdr = await conn.ExecuteReaderAsync(upsertCommand, System.Data.CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    if (rdr[1] == DBNull.Value)
                         item.modified = item.created;
                    else
                    {
                         long modified = (long) rdr[1];
                         item.modified = new UnixTimeUtc((long)modified);
                    }
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableAttestationStatusCRUD", item.attestationId.ToBase64(), item);
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<int> UpdateAsync(DatabaseConnection conn, AttestationStatusRecord item)
        {
            using (var updateCommand = conn.db.CreateCommand())
            {
                string sqlNowStr;
                sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)"; // Needs _scopedConnectionFactory to support Postgres
                updateCommand.CommandText = "UPDATE AttestationStatus " +
                                            $"SET status = @status,modified = MAX(AttestationStatus.modified+1,{sqlNowStr}) "+
                                            "WHERE (attestationId = @attestationId) "+
                                            "RETURNING created,modified,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@attestationId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.Int32;
                updateParam2.ParameterName = "@status";
                updateCommand.Parameters.Add(updateParam2);
                updateParam1.Value = item.attestationId;
                updateParam2.Value = item.status;
                await using var rdr = await conn.ExecuteReaderAsync(updateCommand, System.Data.CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    if (rdr[1] == DBNull.Value)
                         item.modified = item.created;
                    else
                    {
                         long modified = (long) rdr[1];
                         item.modified = new UnixTimeUtc((long)modified);
                    }
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableAttestationStatusCRUD", item.attestationId.ToBase64(), item);
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<int> GetCountAsync(DatabaseConnection conn)
        {
            using (var getCountCommand = conn.db.CreateCommand())
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM AttestationStatus;";
                var count = await conn.ExecuteScalarAsync(getCountCommand);
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("attestationId");
            sl.Add("status");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,attestationId,status,created,modified
        public AttestationStatusRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<AttestationStatusRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AttestationStatusRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.attestationIdNoLengthCheck = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[1]);
            if (item.attestationId?.Length < 16)
                throw new Exception("Too little data in attestationId...");
            item.status = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[2];
            item.created = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.modified = (rdr[4] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[4]); // HACK
            return item;
       }

        public virtual async Task<int> DeleteAsync(DatabaseConnection conn, byte[] attestationId)
        {
            if (attestationId == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
            if (attestationId?.Length < 16) throw new OdinDatabaseValidationException($"Too short attestationId, was {attestationId.Length} (min 16)");
            if (attestationId?.Length > 64) throw new OdinDatabaseValidationException($"Too long attestationId, was {attestationId.Length} (max 64)");
            using (var delete0Command = conn.db.CreateCommand())
            {
                delete0Command.CommandText = "DELETE FROM AttestationStatus " +
                                             "WHERE attestationId = @attestationId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@attestationId";
                delete0Command.Parameters.Add(delete0Param1);

                delete0Param1.Value = attestationId;
                var count = await conn.ExecuteNonQueryAsync(delete0Command);
                if (count > 0)
                    _cache.Remove("TableAttestationStatusCRUD", attestationId.ToBase64());
                return count;
            }
        }

        public AttestationStatusRecord ReadRecordFromReader0(DbDataReader rdr,byte[] attestationId)
        {
            if (attestationId == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
            if (attestationId?.Length < 16) throw new OdinDatabaseValidationException($"Too short attestationId, was {attestationId.Length} (min 16)");
            if (attestationId?.Length > 64) throw new OdinDatabaseValidationException($"Too long attestationId, was {attestationId.Length} (max 64)");
            var result = new List<AttestationStatusRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AttestationStatusRecord();
            item.attestationId = attestationId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.status = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[1];
            item.created = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);
            item.modified = (rdr[3] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[3]); // HACK
            return item;
       }

        public virtual async Task<AttestationStatusRecord> GetAsync(DatabaseConnection conn,byte[] attestationId)
        {
            if (attestationId == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
            if (attestationId?.Length < 16) throw new OdinDatabaseValidationException($"Too short attestationId, was {attestationId.Length} (min 16)");
            if (attestationId?.Length > 64) throw new OdinDatabaseValidationException($"Too long attestationId, was {attestationId.Length} (max 64)");
            var (hit, cacheObject) = _cache.Get("TableAttestationStatusCRUD", attestationId.ToBase64());
            if (hit)
                return (AttestationStatusRecord)cacheObject;
            using (var get0Command = conn.db.CreateCommand())
            {
                get0Command.CommandText = "SELECT rowId,status,created,modified FROM AttestationStatus " +
                                             "WHERE attestationId = @attestationId LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
                get0Param1.ParameterName = "@attestationId";
                get0Command.Parameters.Add(get0Param1);

                get0Param1.Value = attestationId;
                {
                    using (var rdr = await conn.ExecuteReaderAsync(get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableAttestationStatusCRUD", attestationId.ToBase64(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,attestationId);
                        _cache.AddOrUpdate("TableAttestationStatusCRUD", attestationId.ToBase64(), r);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
