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
using Odin.Core.Util;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class InboxRecord
    {
        private Guid _identityId;
        public Guid identityId
        {
           get {
                   return _identityId;
               }
           set {
                  _identityId = value;
               }
        }
        private Guid _fileId;
        public Guid fileId
        {
           get {
                   return _fileId;
               }
           set {
                  _fileId = value;
               }
        }
        private Guid _boxId;
        public Guid boxId
        {
           get {
                   return _boxId;
               }
           set {
                  _boxId = value;
               }
        }
        private Int32 _priority;
        public Int32 priority
        {
           get {
                   return _priority;
               }
           set {
                  _priority = value;
               }
        }
        private UnixTimeUtc _timeStamp;
        public UnixTimeUtc timeStamp
        {
           get {
                   return _timeStamp;
               }
           set {
                  _timeStamp = value;
               }
        }
        private byte[] _value;
        public byte[] value
        {
           get {
                   return _value;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _value = value;
               }
        }
        private Guid? _popStamp;
        public Guid? popStamp
        {
           get {
                   return _popStamp;
               }
           set {
                  _popStamp = value;
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
    } // End of class InboxRecord

    public abstract class TableInboxCRUD
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableInboxCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            {
                if (dropExisting)
                {
                   cmd.CommandText = "DROP TABLE IF EXISTS inbox;";
                   await cmd.ExecuteNonQueryAsync();
                }
                cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS inbox("
                 +"identityId BLOB NOT NULL, "
                 +"fileId BLOB NOT NULL UNIQUE, "
                 +"boxId BLOB NOT NULL, "
                 +"priority INT NOT NULL, "
                 +"timeStamp INT NOT NULL, "
                 +"value BLOB , "
                 +"popStamp BLOB , "
                 +"created INT NOT NULL, "
                 +"modified INT  "
                 +", PRIMARY KEY (identityId,fileId)"
                 +");"
                 +"CREATE INDEX IF NOT EXISTS Idx0TableInboxCRUD ON inbox(identityId,timeStamp);"
                 +"CREATE INDEX IF NOT EXISTS Idx1TableInboxCRUD ON inbox(identityId,boxId);"
                 +"CREATE INDEX IF NOT EXISTS Idx2TableInboxCRUD ON inbox(identityId,popStamp);"
                 ;
                 await cmd.ExecuteNonQueryAsync();
            }
        }

        public virtual async Task<int> InsertAsync(InboxRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.boxId.AssertGuidNotEmpty("Guid parameter boxId cannot be set to Empty GUID.");
            item.popStamp.AssertGuidNotEmpty("Guid parameter popStamp cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO inbox (identityId,fileId,boxId,priority,timeStamp,value,popStamp,created,modified) " +
                                             "VALUES (@identityId,@fileId,@boxId,@priority,@timeStamp,@value,@popStamp,@created,@modified)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@fileId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@boxId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@priority";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@timeStamp";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.ParameterName = "@value";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@popStamp";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam9);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.fileId.ToByteArray();
                insertParam3.Value = item.boxId.ToByteArray();
                insertParam4.Value = item.priority;
                insertParam5.Value = item.timeStamp.milliseconds;
                insertParam6.Value = item.value ?? (object)DBNull.Value;
                insertParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                insertParam8.Value = now.uniqueTime;
                item.modified = null;
                insertParam9.Value = DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                     item.created = now;
                }
                return count;
            }
        }

        public virtual async Task<int> TryInsertAsync(InboxRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.boxId.AssertGuidNotEmpty("Guid parameter boxId cannot be set to Empty GUID.");
            item.popStamp.AssertGuidNotEmpty("Guid parameter popStamp cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT OR IGNORE INTO inbox (identityId,fileId,boxId,priority,timeStamp,value,popStamp,created,modified) " +
                                             "VALUES (@identityId,@fileId,@boxId,@priority,@timeStamp,@value,@popStamp,@created,@modified)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@fileId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@boxId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@priority";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@timeStamp";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.ParameterName = "@value";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@popStamp";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam9);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.fileId.ToByteArray();
                insertParam3.Value = item.boxId.ToByteArray();
                insertParam4.Value = item.priority;
                insertParam5.Value = item.timeStamp.milliseconds;
                insertParam6.Value = item.value ?? (object)DBNull.Value;
                insertParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                insertParam8.Value = now.uniqueTime;
                item.modified = null;
                insertParam9.Value = DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    item.created = now;
                }
                return count;
            }
        }

        public virtual async Task<int> UpsertAsync(InboxRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.boxId.AssertGuidNotEmpty("Guid parameter boxId cannot be set to Empty GUID.");
            item.popStamp.AssertGuidNotEmpty("Guid parameter popStamp cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO inbox (identityId,fileId,boxId,priority,timeStamp,value,popStamp,created) " +
                                             "VALUES (@identityId,@fileId,@boxId,@priority,@timeStamp,@value,@popStamp,@created)"+
                                             "ON CONFLICT (identityId,fileId) DO UPDATE "+
                                             "SET boxId = @boxId,priority = @priority,timeStamp = @timeStamp,value = @value,popStamp = @popStamp,modified = @modified "+
                                             "RETURNING created, modified;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@fileId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@boxId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@priority";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.ParameterName = "@timeStamp";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.ParameterName = "@value";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.ParameterName = "@popStamp";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.ParameterName = "@created";
                upsertCommand.Parameters.Add(upsertParam8);
                var upsertParam9 = upsertCommand.CreateParameter();
                upsertParam9.ParameterName = "@modified";
                upsertCommand.Parameters.Add(upsertParam9);
                var now = UnixTimeUtcUnique.Now();
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.fileId.ToByteArray();
                upsertParam3.Value = item.boxId.ToByteArray();
                upsertParam4.Value = item.priority;
                upsertParam5.Value = item.timeStamp.milliseconds;
                upsertParam6.Value = item.value ?? (object)DBNull.Value;
                upsertParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                upsertParam8.Value = now.uniqueTime;
                upsertParam9.Value = now.uniqueTime;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                   long created = rdr.GetInt64(0);
                   long? modified = rdr.IsDBNull(1) ? null : rdr.GetInt64(1);
                   item.created = new UnixTimeUtcUnique(created);
                   if (modified != null)
                      item.modified = new UnixTimeUtcUnique((long)modified);
                   else
                      item.modified = null;
                   return 1;
                }
                return 0;
            }
        }

        public virtual async Task<int> UpdateAsync(InboxRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.boxId.AssertGuidNotEmpty("Guid parameter boxId cannot be set to Empty GUID.");
            item.popStamp.AssertGuidNotEmpty("Guid parameter popStamp cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE inbox " +
                                             "SET boxId = @boxId,priority = @priority,timeStamp = @timeStamp,value = @value,popStamp = @popStamp,modified = @modified "+
                                             "WHERE (identityId = @identityId AND fileId = @fileId)";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@fileId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@boxId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@priority";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.ParameterName = "@timeStamp";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.ParameterName = "@value";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.ParameterName = "@popStamp";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.ParameterName = "@created";
                updateCommand.Parameters.Add(updateParam8);
                var updateParam9 = updateCommand.CreateParameter();
                updateParam9.ParameterName = "@modified";
                updateCommand.Parameters.Add(updateParam9);
                var now = UnixTimeUtcUnique.Now();
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.fileId.ToByteArray();
                updateParam3.Value = item.boxId.ToByteArray();
                updateParam4.Value = item.priority;
                updateParam5.Value = item.timeStamp.milliseconds;
                updateParam6.Value = item.value ?? (object)DBNull.Value;
                updateParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                updateParam8.Value = now.uniqueTime;
                updateParam9.Value = now.uniqueTime;
                var count = await updateCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                     item.modified = now;
                }
                return count;
            }
        }

        public virtual async Task<int> GetCountDirtyAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM inbox; PRAGMA read_uncommitted = 0;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("identityId");
            sl.Add("fileId");
            sl.Add("boxId");
            sl.Add("priority");
            sl.Add("timeStamp");
            sl.Add("value");
            sl.Add("popStamp");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT identityId,fileId,boxId,priority,timeStamp,value,popStamp,created,modified
        public InboxRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<InboxRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new InboxRecord();
            item.identityId = rdr.IsDBNull(0) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.fileId = rdr.IsDBNull(1) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.boxId = rdr.IsDBNull(2) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.priority = rdr.IsDBNull(3) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[3];
            item.timeStamp = rdr.IsDBNull(4) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[4]);
            item.value = rdr.IsDBNull(5) ? 
                null : (byte[])(rdr[5]);
            if (item.value.Length > 65535)
                throw new Exception("Too much data in value...");
            if (item.value.Length < 0)
                throw new Exception("Too little data in value...");
            item.popStamp = rdr.IsDBNull(6) ? 
                null : new Guid((byte[])rdr[6]);
            item.created = rdr.IsDBNull(7) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtcUnique((long)rdr[7]);
            item.modified = rdr.IsDBNull(8) ? 
                null : new UnixTimeUtcUnique((long)rdr[8]);
            return item;
       }

        public virtual async Task<int> DeleteAsync(Guid identityId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM inbox " +
                                             "WHERE identityId = @identityId AND fileId = @fileId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@fileId";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = fileId.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        public InboxRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,Guid fileId)
        {
            var result = new List<InboxRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new InboxRecord();
            item.identityId = identityId;
            item.fileId = fileId;

            item.boxId = rdr.IsDBNull(0) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);

            item.priority = rdr.IsDBNull(1) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[1];

            item.timeStamp = rdr.IsDBNull(2) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);

            item.value = rdr.IsDBNull(3) ? 
                null : (byte[])(rdr[3]);
            if (item.value.Length > 65535)
                throw new Exception("Too much data in value...");
            if (item.value.Length < 0)
                throw new Exception("Too little data in value...");

            item.popStamp = rdr.IsDBNull(4) ? 
                null : new Guid((byte[])rdr[4]);

            item.created = rdr.IsDBNull(5) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtcUnique((long)rdr[5]);

            item.modified = rdr.IsDBNull(6) ? 
                null : new UnixTimeUtcUnique((long)rdr[6]);
            return item;
       }

        public virtual async Task<InboxRecord> GetAsync(Guid identityId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT boxId,priority,timeStamp,value,popStamp,created,modified FROM inbox " +
                                             "WHERE identityId = @identityId AND fileId = @fileId LIMIT 1;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@fileId";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = fileId.ToByteArray();
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identityId,fileId);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
