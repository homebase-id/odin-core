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

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class ConnectionsRecord
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
        private OdinId _identity;
        public OdinId identity
        {
           get {
                   return _identity;
               }
           set {
                  _identity = value;
               }
        }
        private string _displayName;
        public string displayName
        {
           get {
                   return _displayName;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 80) throw new Exception("Too long");
                  _displayName = value;
               }
        }
        internal string displayNameNoLengthCheck
        {
           get {
                   return _displayName;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                  _displayName = value;
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
        private Int32 _accessIsRevoked;
        public Int32 accessIsRevoked
        {
           get {
                   return _accessIsRevoked;
               }
           set {
                  _accessIsRevoked = value;
               }
        }
        private byte[] _data;
        public byte[] data
        {
           get {
                   return _data;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _data = value;
               }
        }
        internal byte[] dataNoLengthCheck
        {
           get {
                   return _data;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                  _data = value;
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
        private UnixTimeUtc? _modified;
        public UnixTimeUtc? modified
        {
           get {
                   return _modified;
               }
           set {
                  _modified = value;
               }
        }
    } // End of class ConnectionsRecord

    public abstract class TableConnectionsCRUD
    {
        private readonly CacheHelper _cache;
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableConnectionsCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _cache = cache;
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS connections;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS connections("
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"identity TEXT NOT NULL, "
                   +"displayName TEXT NOT NULL, "
                   +"status BIGINT NOT NULL, "
                   +"accessIsRevoked BIGINT NOT NULL, "
                   +"data BYTEA , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT  "
                   +", UNIQUE(identityId,identity)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0TableConnectionsCRUD ON connections(identityId,created);"
                   ;
            await cmd.ExecuteNonQueryAsync();
        }

        protected virtual async Task<int> InsertAsync(ConnectionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO connections (identityId,identity,displayName,status,accessIsRevoked,data,created,modified) " +
                                             "VALUES (@identityId,@identity,@displayName,@status,@accessIsRevoked,@data,@created,@modified)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@displayName";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@status";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@accessIsRevoked";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.identity.DomainName;
                insertParam3.Value = item.displayName;
                insertParam4.Value = item.status;
                insertParam5.Value = item.accessIsRevoked;
                insertParam6.Value = item.data ?? (object)DBNull.Value;
                var now = UnixTimeUtc.Now();
                insertParam7.Value = now.milliseconds;
                item.modified = null;
                insertParam8.Value = DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                     item.created = now;
                    _cache.AddOrUpdate("TableConnectionsCRUD", item.identityId.ToString()+item.identity.DomainName, item);
                }
                return count;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(ConnectionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO connections (identityId,identity,displayName,status,accessIsRevoked,data,created,modified) " +
                                             "VALUES (@identityId,@identity,@displayName,@status,@accessIsRevoked,@data,@created,@modified) " +
                                             "ON CONFLICT DO NOTHING";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@displayName";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@status";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@accessIsRevoked";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.identity.DomainName;
                insertParam3.Value = item.displayName;
                insertParam4.Value = item.status;
                insertParam5.Value = item.accessIsRevoked;
                insertParam6.Value = item.data ?? (object)DBNull.Value;
                var now = UnixTimeUtc.Now();
                insertParam7.Value = now.milliseconds;
                item.modified = null;
                insertParam8.Value = DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    item.created = now;
                   _cache.AddOrUpdate("TableConnectionsCRUD", item.identityId.ToString()+item.identity.DomainName, item);
                }
                return count > 0;
            }
        }

        protected virtual async Task<int> UpsertAsync(ConnectionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO connections (identityId,identity,displayName,status,accessIsRevoked,data,created) " +
                                             "VALUES (@identityId,@identity,@displayName,@status,@accessIsRevoked,@data,@created)"+
                                             "ON CONFLICT (identityId,identity) DO UPDATE "+
                                             "SET displayName = @displayName,status = @status,accessIsRevoked = @accessIsRevoked,data = @data,modified = @modified "+
                                             "RETURNING created, modified;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@identity";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@displayName";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@status";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.ParameterName = "@accessIsRevoked";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.ParameterName = "@data";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.ParameterName = "@created";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.ParameterName = "@modified";
                upsertCommand.Parameters.Add(upsertParam8);
                var now = UnixTimeUtc.Now();
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.identity.DomainName;
                upsertParam3.Value = item.displayName;
                upsertParam4.Value = item.status;
                upsertParam5.Value = item.accessIsRevoked;
                upsertParam6.Value = item.data ?? (object)DBNull.Value;
                upsertParam7.Value = now.milliseconds;
                upsertParam8.Value = now.milliseconds;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                   long created = (long) rdr[0];
                   long? modified = (rdr[1] == DBNull.Value) ? null : (long) rdr[1];
                   item.created = new UnixTimeUtc(created);
                   if (modified != null)
                      item.modified = new UnixTimeUtc((long)modified);
                   else
                      item.modified = null;
                   _cache.AddOrUpdate("TableConnectionsCRUD", item.identityId.ToString()+item.identity.DomainName, item);
                   return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(ConnectionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE connections " +
                                             "SET displayName = @displayName,status = @status,accessIsRevoked = @accessIsRevoked,data = @data,modified = @modified "+
                                             "WHERE (identityId = @identityId AND identity = @identity)";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@identity";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@displayName";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@status";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.ParameterName = "@accessIsRevoked";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.ParameterName = "@data";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.ParameterName = "@created";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.ParameterName = "@modified";
                updateCommand.Parameters.Add(updateParam8);
                var now = UnixTimeUtc.Now();
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.identity.DomainName;
                updateParam3.Value = item.displayName;
                updateParam4.Value = item.status;
                updateParam5.Value = item.accessIsRevoked;
                updateParam6.Value = item.data ?? (object)DBNull.Value;
                updateParam7.Value = now.milliseconds;
                updateParam8.Value = now.milliseconds;
                var count = await updateCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                     item.modified = now;
                    _cache.AddOrUpdate("TableConnectionsCRUD", item.identityId.ToString()+item.identity.DomainName, item);
                }
                return count;
            }
        }

        protected virtual async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM connections;";
                var count = await getCountCommand.ExecuteScalarAsync();
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
            sl.Add("identityId");
            sl.Add("identity");
            sl.Add("displayName");
            sl.Add("status");
            sl.Add("accessIsRevoked");
            sl.Add("data");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,identityId,identity,displayName,status,accessIsRevoked,data,created,modified
        protected ConnectionsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<ConnectionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new ConnectionsRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.identity = (rdr[2] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[2]);
            item.displayNameNoLengthCheck = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.status = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[4];
            item.accessIsRevoked = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[5];
            item.dataNoLengthCheck = (rdr[6] == DBNull.Value) ? null : (byte[])(rdr[6]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            item.created = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            item.modified = (rdr[8] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[8]);
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,OdinId identity)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM connections " +
                                             "WHERE identityId = @identityId AND identity = @identity";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@identity";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = identity.DomainName;
                var count = await delete0Command.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.Remove("TableConnectionsCRUD", identityId.ToString()+identity.DomainName);
                return count;
            }
        }

        protected ConnectionsRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,OdinId identity)
        {
            var result = new List<ConnectionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new ConnectionsRecord();
            item.identityId = identityId;
            item.identity = identity;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.displayNameNoLengthCheck = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.status = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[2];
            item.accessIsRevoked = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[3];
            item.dataNoLengthCheck = (rdr[4] == DBNull.Value) ? null : (byte[])(rdr[4]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            item.created = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.modified = (rdr[6] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[6]);
            return item;
       }

        protected virtual async Task<ConnectionsRecord> GetAsync(Guid identityId,OdinId identity)
        {
            var (hit, cacheObject) = _cache.Get("TableConnectionsCRUD", identityId.ToString()+identity.DomainName);
            if (hit)
                return (ConnectionsRecord)cacheObject;
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                             "WHERE identityId = @identityId AND identity = @identity LIMIT 1;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@identity";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = identity.DomainName;
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableConnectionsCRUD", identityId.ToString()+identity.DomainName, null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identityId,identity);
                        _cache.AddOrUpdate("TableConnectionsCRUD", identityId.ToString()+identity.DomainName, r);
                        return r;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<ConnectionsRecord>, string nextCursor)> PagingByIdentityAsync(int count, Guid identityId, string inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (count == int.MaxValue)
                count--; // avoid overflow when doing +1 on the param below
            if (inCursor == null)
                inCursor = "";

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging2Command = cn.CreateCommand();
            {
                getPaging2Command.CommandText = "SELECT rowId,identityId,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                            "WHERE (identityId = @identityId) AND identity > @identity  ORDER BY identity ASC  LIMIT @count;";
                var getPaging2Param1 = getPaging2Command.CreateParameter();
                getPaging2Param1.ParameterName = "@identity";
                getPaging2Command.Parameters.Add(getPaging2Param1);
                var getPaging2Param2 = getPaging2Command.CreateParameter();
                getPaging2Param2.ParameterName = "@count";
                getPaging2Command.Parameters.Add(getPaging2Param2);
                var getPaging2Param3 = getPaging2Command.CreateParameter();
                getPaging2Param3.ParameterName = "@identityId";
                getPaging2Command.Parameters.Add(getPaging2Param3);

                getPaging2Param1.Value = inCursor;
                getPaging2Param2.Value = count+1;
                getPaging2Param3.Value = identityId.ToByteArray();

                {
                    await using (var rdr = await getPaging2Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<ConnectionsRecord>();
                        string nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].identity;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return (result, nextCursor);
                    } // using
                } //
            } // using 
        } // PagingGet

        protected virtual async Task<(List<ConnectionsRecord>, string nextCursor)> PagingByIdentityAsync(int count, Guid identityId,Int32 status, string inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (count == int.MaxValue)
                count--; // avoid overflow when doing +1 on the param below
            if (inCursor == null)
                inCursor = "";

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging2Command = cn.CreateCommand();
            {
                getPaging2Command.CommandText = "SELECT rowId,identityId,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                            "WHERE (identityId = @identityId AND status = @status) AND identity > @identity  ORDER BY identity ASC  LIMIT @count;";
                var getPaging2Param1 = getPaging2Command.CreateParameter();
                getPaging2Param1.ParameterName = "@identity";
                getPaging2Command.Parameters.Add(getPaging2Param1);
                var getPaging2Param2 = getPaging2Command.CreateParameter();
                getPaging2Param2.ParameterName = "@count";
                getPaging2Command.Parameters.Add(getPaging2Param2);
                var getPaging2Param3 = getPaging2Command.CreateParameter();
                getPaging2Param3.ParameterName = "@identityId";
                getPaging2Command.Parameters.Add(getPaging2Param3);
                var getPaging2Param4 = getPaging2Command.CreateParameter();
                getPaging2Param4.ParameterName = "@status";
                getPaging2Command.Parameters.Add(getPaging2Param4);

                getPaging2Param1.Value = inCursor;
                getPaging2Param2.Value = count+1;
                getPaging2Param3.Value = identityId.ToByteArray();
                getPaging2Param4.Value = status;

                {
                    await using (var rdr = await getPaging2Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<ConnectionsRecord>();
                        string nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].identity;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return (result, nextCursor);
                    } // using
                } //
            } // using 
        } // PagingGet

        protected virtual async Task<(List<ConnectionsRecord>, UnixTimeUtc? nextCursor, long nextRowId)> PagingByCreatedAsync(int count, Guid identityId,Int32 status, UnixTimeUtc? inCursor, long? rowid)
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
            await using var getPaging7Command = cn.CreateCommand();
            {
                getPaging7Command.CommandText = "SELECT rowId,identityId,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                            "WHERE (identityId = @identityId AND status = @status) AND created <= @created AND rowId < @rowId ORDER BY created DESC , rowId DESC LIMIT @count;";
                var getPaging7Param1 = getPaging7Command.CreateParameter();
                getPaging7Param1.ParameterName = "@created";
                getPaging7Command.Parameters.Add(getPaging7Param1);
                var getPaging7Param2 = getPaging7Command.CreateParameter();
                getPaging7Param2.ParameterName = "@rowId";
                getPaging7Command.Parameters.Add(getPaging7Param2);
                var getPaging7Param3 = getPaging7Command.CreateParameter();
                getPaging7Param3.ParameterName = "@count";
                getPaging7Command.Parameters.Add(getPaging7Param3);
                var getPaging7Param4 = getPaging7Command.CreateParameter();
                getPaging7Param4.ParameterName = "@identityId";
                getPaging7Command.Parameters.Add(getPaging7Param4);
                var getPaging7Param5 = getPaging7Command.CreateParameter();
                getPaging7Param5.ParameterName = "@status";
                getPaging7Command.Parameters.Add(getPaging7Param5);

                getPaging7Param1.Value = inCursor?.milliseconds;
                getPaging7Param2.Value = rowid;
                getPaging7Param3.Value = count+1;
                getPaging7Param4.Value = identityId.ToByteArray();
                getPaging7Param5.Value = status;

                {
                    await using (var rdr = await getPaging7Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<ConnectionsRecord>();
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

        protected virtual async Task<(List<ConnectionsRecord>, UnixTimeUtc? nextCursor, long nextRowId)> PagingByCreatedAsync(int count, Guid identityId, UnixTimeUtc? inCursor, long? rowid)
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
            await using var getPaging7Command = cn.CreateCommand();
            {
                getPaging7Command.CommandText = "SELECT rowId,identityId,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                            "WHERE (identityId = @identityId) AND created <= @created AND rowId < @rowId ORDER BY created DESC , rowId DESC LIMIT @count;";
                var getPaging7Param1 = getPaging7Command.CreateParameter();
                getPaging7Param1.ParameterName = "@created";
                getPaging7Command.Parameters.Add(getPaging7Param1);
                var getPaging7Param2 = getPaging7Command.CreateParameter();
                getPaging7Param2.ParameterName = "@rowId";
                getPaging7Command.Parameters.Add(getPaging7Param2);
                var getPaging7Param3 = getPaging7Command.CreateParameter();
                getPaging7Param3.ParameterName = "@count";
                getPaging7Command.Parameters.Add(getPaging7Param3);
                var getPaging7Param4 = getPaging7Command.CreateParameter();
                getPaging7Param4.ParameterName = "@identityId";
                getPaging7Command.Parameters.Add(getPaging7Param4);

                getPaging7Param1.Value = inCursor?.milliseconds;
                getPaging7Param2.Value = rowid;
                getPaging7Param3.Value = count+1;
                getPaging7Param4.Value = identityId.ToByteArray();

                {
                    await using (var rdr = await getPaging7Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<ConnectionsRecord>();
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
