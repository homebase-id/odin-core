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

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.SQLite.AttestationDatabase
{
    public class AttestationStatusRecord
    {
        private byte[] _attestationId;
        public byte[] attestationId
        {
           get {
                   return _attestationId;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 64) throw new Exception("Too long");
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
        private UnixTimeUtcUnique _created;
        public UnixTimeUtcUnique created
        {
           get {
                   return _created;
               }
           set {
                  _created = value;
               }
        }
        private UnixTimeUtcUnique? _modified;
        public UnixTimeUtcUnique? modified
        {
           get {
                   return _modified;
               }
           set {
                  _modified = value;
               }
        }
    } // End of class AttestationStatusRecord

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
                cmd.CommandText = "DROP TABLE IF EXISTS attestationStatus;";
                await conn.ExecuteNonQueryAsync(cmd);
            }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS attestationStatus("
                   +"attestationId BLOB NOT NULL UNIQUE, "
                   +"status INT NOT NULL, "
                   +"created INT NOT NULL, "
                   +"modified INT  "
                   +", PRIMARY KEY (attestationId)"
                   +");"
                   ;
            await conn.ExecuteNonQueryAsync(cmd);
        }

        public virtual async Task<int> InsertAsync(DatabaseConnection conn, AttestationStatusRecord item)
        {
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT INTO attestationStatus (attestationId,status,created,modified) " +
                                             "VALUES (@attestationId,@status,@created,@modified)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@attestationId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@status";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.attestationId;
                insertParam2.Value = item.status;
                var now = UnixTimeUtcUnique.Now();
                insertParam3.Value = now.uniqueTime;
                item.modified = null;
                insertParam4.Value = DBNull.Value;
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                     item.created = now;
                    _cache.AddOrUpdate("TableAttestationStatusCRUD", item.attestationId.ToBase64(), item);
                }
                return count;
            }
        }

        public virtual async Task<bool> TryInsertAsync(DatabaseConnection conn, AttestationStatusRecord item)
        {
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT INTO attestationStatus (attestationId,status,created,modified) " +
                                             "VALUES (@attestationId,@status,@created,@modified) " +
                                             "ON CONFLICT DO NOTHING";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@attestationId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@status";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.attestationId;
                insertParam2.Value = item.status;
                var now = UnixTimeUtcUnique.Now();
                insertParam3.Value = now.uniqueTime;
                item.modified = null;
                insertParam4.Value = DBNull.Value;
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                    item.created = now;
                   _cache.AddOrUpdate("TableAttestationStatusCRUD", item.attestationId.ToBase64(), item);
                }
                return count > 0;
            }
        }

        public virtual async Task<int> UpsertAsync(DatabaseConnection conn, AttestationStatusRecord item)
        {
            using (var upsertCommand = conn.db.CreateCommand())
            {
                upsertCommand.CommandText = "INSERT INTO attestationStatus (attestationId,status,created) " +
                                             "VALUES (@attestationId,@status,@created)"+
                                             "ON CONFLICT (attestationId) DO UPDATE "+
                                             "SET status = @status,modified = @modified "+
                                             "RETURNING created, modified;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@attestationId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@status";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@created";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@modified";
                upsertCommand.Parameters.Add(upsertParam4);
                var now = UnixTimeUtcUnique.Now();
                upsertParam1.Value = item.attestationId;
                upsertParam2.Value = item.status;
                upsertParam3.Value = now.uniqueTime;
                upsertParam4.Value = now.uniqueTime;
                await using var rdr = await conn.ExecuteReaderAsync(upsertCommand, System.Data.CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                   long created = rdr.GetInt64(0);
                   long? modified = rdr.IsDBNull(1) ? null : rdr.GetInt64(1);
                   item.created = new UnixTimeUtcUnique(created);
                   if (modified != null)
                      item.modified = new UnixTimeUtcUnique((long)modified);
                   else
                      item.modified = null;
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
                updateCommand.CommandText = "UPDATE attestationStatus " +
                                             "SET status = @status,modified = @modified "+
                                             "WHERE (attestationId = @attestationId)";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@attestationId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@status";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@created";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@modified";
                updateCommand.Parameters.Add(updateParam4);
                var now = UnixTimeUtcUnique.Now();
                updateParam1.Value = item.attestationId;
                updateParam2.Value = item.status;
                updateParam3.Value = now.uniqueTime;
                updateParam4.Value = now.uniqueTime;
                var count = await conn.ExecuteNonQueryAsync(updateCommand);
                if (count > 0)
                {
                     item.modified = now;
                    _cache.AddOrUpdate("TableAttestationStatusCRUD", item.attestationId.ToBase64(), item);
                }
                return count;
            }
        }

        public virtual async Task<int> GetCountDirtyAsync(DatabaseConnection conn)
        {
            using (var getCountCommand = conn.db.CreateCommand())
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM attestationStatus; PRAGMA read_uncommitted = 0;";
                var count = await conn.ExecuteScalarAsync(getCountCommand);
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("attestationId");
            sl.Add("status");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT attestationId,status,created,modified
        public AttestationStatusRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<AttestationStatusRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AttestationStatusRecord();
            item.attestationId = rdr.IsDBNull(0) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[0]);
            if (item.attestationId?.Length > 64)
                throw new Exception("Too much data in attestationId...");
            if (item.attestationId?.Length < 16)
                throw new Exception("Too little data in attestationId...");
            item.status = rdr.IsDBNull(1) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[1];
            item.created = rdr.IsDBNull(2) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtcUnique((long)rdr[2]);
            item.modified = rdr.IsDBNull(3) ? 
                null : new UnixTimeUtcUnique((long)rdr[3]);
            return item;
       }

        public virtual async Task<int> DeleteAsync(DatabaseConnection conn, byte[] attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 16) throw new Exception("Too short");
            if (attestationId?.Length > 64) throw new Exception("Too long");
            using (var delete0Command = conn.db.CreateCommand())
            {
                delete0Command.CommandText = "DELETE FROM attestationStatus " +
                                             "WHERE attestationId = @attestationId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@attestationId";
                delete0Command.Parameters.Add(delete0Param1);

                delete0Param1.Value = attestationId;
                var count = await conn.ExecuteNonQueryAsync(delete0Command);
                if (count > 0)
                    _cache.Remove("TableAttestationStatusCRUD", attestationId.ToBase64());
                return count;
            }
        }

        public AttestationStatusRecord ReadRecordFromReader0(DbDataReader rdr, byte[] attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 16) throw new Exception("Too short");
            if (attestationId?.Length > 64) throw new Exception("Too long");
            var result = new List<AttestationStatusRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AttestationStatusRecord();
            item.attestationId = attestationId;

            item.status = rdr.IsDBNull(0) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[0];

            item.created = rdr.IsDBNull(1) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtcUnique((long)rdr[1]);

            item.modified = rdr.IsDBNull(2) ? 
                null : new UnixTimeUtcUnique((long)rdr[2]);
            return item;
       }

        public virtual async Task<AttestationStatusRecord> GetAsync(DatabaseConnection conn, byte[] attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 16) throw new Exception("Too short");
            if (attestationId?.Length > 64) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableAttestationStatusCRUD", attestationId.ToBase64());
            if (hit)
                return (AttestationStatusRecord)cacheObject;
            using (var get0Command = conn.db.CreateCommand())
            {
                get0Command.CommandText = "SELECT status,created,modified FROM attestationStatus " +
                                             "WHERE attestationId = @attestationId LIMIT 1;";
                var get0Param1 = get0Command.CreateParameter();
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
                        var r = ReadRecordFromReader0(rdr, attestationId);
                        _cache.AddOrUpdate("TableAttestationStatusCRUD", attestationId.ToBase64(), r);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
