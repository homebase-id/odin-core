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

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record DriveDefinitionsRecord
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
        private Guid _driveId;
        public Guid driveId
        {
           get {
                   return _driveId;
               }
           set {
                  _driveId = value;
               }
        }
        private Guid _driveType;
        public Guid driveType
        {
           get {
                   return _driveType;
               }
           set {
                  _driveType = value;
               }
        }
        private string _data;
        public string data
        {
           get {
                   return _data;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null data");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short data, was {value.Length} (min 0)");
                    if (value?.Length > 21504) throw new OdinDatabaseValidationException($"Too long data, was {value.Length} (max 21504)");
                  _data = value;
               }
        }
        internal string dataNoLengthCheck
        {
           get {
                   return _data;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null data");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short data, was {value.Length} (min 0)");
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
    } // End of record DriveDefinitionsRecord

    public abstract class TableDriveDefinitionsCRUD
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableDriveDefinitionsCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS DriveDefinitions;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS DriveDefinitions("
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"driveType BYTEA NOT NULL, "
                   +"data TEXT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT  "
                   +", UNIQUE(identityId,driveId,driveType)"
                   +$"){wori};"
                   ;
            await cmd.ExecuteNonQueryAsync();
        }

        protected virtual async Task<int> InsertAsync(DriveDefinitionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.driveType.AssertGuidNotEmpty("Guid parameter driveType cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr;
                if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
                    sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
                else
                    sqlNowStr = "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
                insertCommand.CommandText = "INSERT INTO DriveDefinitions (identityId,driveId,driveType,data,created,modified) " +
                                             $"VALUES (@identityId,@driveId,@driveType,@data,{sqlNowStr},NULL)"+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@driveType";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.driveId.ToByteArray();
                insertParam3.Value = item.driveType.ToByteArray();
                insertParam4.Value = item.data;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    long? modified = (rdr[1] == DBNull.Value) ? null : (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    if (modified != null)
                        item.modified = new UnixTimeUtc((long)modified);
                    else
                        item.modified = null;
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(DriveDefinitionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.driveType.AssertGuidNotEmpty("Guid parameter driveType cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr;
                if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
                    sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
                else
                    sqlNowStr = "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
                insertCommand.CommandText = "INSERT INTO DriveDefinitions (identityId,driveId,driveType,data,created,modified) " +
                                            $"VALUES (@identityId,@driveId,@driveType,@data,{sqlNowStr},NULL) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@driveType";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.driveId.ToByteArray();
                insertParam3.Value = item.driveType.ToByteArray();
                insertParam4.Value = item.data;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    long? modified = (rdr[1] == DBNull.Value) ? null : (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    if (modified != null)
                        item.modified = new UnixTimeUtc((long)modified);
                    else
                        item.modified = null;
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(DriveDefinitionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.driveType.AssertGuidNotEmpty("Guid parameter driveType cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr;
                if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
                    sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
                else
                    sqlNowStr = "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
                upsertCommand.CommandText = "INSERT INTO DriveDefinitions (identityId,driveId,driveType,data,created,modified) " +
                                            $"VALUES (@identityId,@driveId,@driveType,@data,{sqlNowStr},NULL)"+
                                            "ON CONFLICT (identityId,driveId,driveType) DO UPDATE "+
                                            $"SET data = @data,modified = {sqlNowStr} "+
                                            "RETURNING created,modified,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@driveId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@driveType";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@data";
                upsertCommand.Parameters.Add(upsertParam4);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.driveId.ToByteArray();
                upsertParam3.Value = item.driveType.ToByteArray();
                upsertParam4.Value = item.data;
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
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(DriveDefinitionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.driveType.AssertGuidNotEmpty("Guid parameter driveType cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr;
                if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
                    sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
                else
                    sqlNowStr = "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
                updateCommand.CommandText = "UPDATE DriveDefinitions " +
                                            $"SET data = @data,modified = {sqlNowStr} "+
                                            "WHERE (identityId = @identityId AND driveId = @driveId AND driveType = @driveType) "+
                                            "RETURNING created,modified,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@driveId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@driveType";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@data";
                updateCommand.Parameters.Add(updateParam4);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.driveId.ToByteArray();
                updateParam3.Value = item.driveType.ToByteArray();
                updateParam4.Value = item.data;
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    long? modified = (rdr[1] == DBNull.Value) ? null : (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    if (modified != null)
                        item.modified = new UnixTimeUtc((long)modified);
                    else
                        item.modified = null;
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM DriveDefinitions;";
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
            sl.Add("driveId");
            sl.Add("driveType");
            sl.Add("data");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,identityId,driveId,driveType,data,created,modified
        protected DriveDefinitionsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<DriveDefinitionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveDefinitionsRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.driveId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.driveType = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.dataNoLengthCheck = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.created = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.modified = (rdr[6] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[6]);
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid driveId,Guid driveType)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM DriveDefinitions " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND driveType = @driveType";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@driveId";
                delete0Command.Parameters.Add(delete0Param2);
                var delete0Param3 = delete0Command.CreateParameter();
                delete0Param3.ParameterName = "@driveType";
                delete0Command.Parameters.Add(delete0Param3);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = driveId.ToByteArray();
                delete0Param3.Value = driveType.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected DriveDefinitionsRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid driveId)
        {
            var result = new List<DriveDefinitionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveDefinitionsRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.driveType = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.dataNoLengthCheck = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.created = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.modified = (rdr[4] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[4]);
            return item;
       }

        protected virtual async Task<DriveDefinitionsRecord> GetByDriveIdAsync(Guid identityId,Guid driveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,driveType,data,created,modified FROM DriveDefinitions " +
                                             "WHERE identityId = @identityId AND driveId = @driveId LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@driveId";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = driveId.ToByteArray();
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,driveId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected DriveDefinitionsRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId,Guid driveType)
        {
            var result = new List<DriveDefinitionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveDefinitionsRecord();
            item.identityId = identityId;
            item.driveType = driveType;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.driveId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.dataNoLengthCheck = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.created = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.modified = (rdr[4] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[4]);
            return item;
       }

        protected virtual async Task<List<DriveDefinitionsRecord>> GetByDriveTypeAsync(Guid identityId,Guid driveType)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,driveId,data,created,modified FROM DriveDefinitions " +
                                             "WHERE identityId = @identityId AND driveType = @driveType;"+
                                             ";";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.ParameterName = "@driveType";
                get1Command.Parameters.Add(get1Param2);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = driveType.ToByteArray();
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<DriveDefinitionsRecord>();
                        }
                        var result = new List<DriveDefinitionsRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr,identityId,driveType));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected DriveDefinitionsRecord ReadRecordFromReader2(DbDataReader rdr,Guid identityId,Guid driveId,Guid driveType)
        {
            var result = new List<DriveDefinitionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveDefinitionsRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.driveType = driveType;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.dataNoLengthCheck = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.created = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);
            item.modified = (rdr[3] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[3]);
            return item;
       }

        protected virtual async Task<DriveDefinitionsRecord> GetAsync(Guid identityId,Guid driveId,Guid driveType)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT rowId,data,created,modified FROM DriveDefinitions " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND driveType = @driveType LIMIT 1;"+
                                             ";";
                var get2Param1 = get2Command.CreateParameter();
                get2Param1.ParameterName = "@identityId";
                get2Command.Parameters.Add(get2Param1);
                var get2Param2 = get2Command.CreateParameter();
                get2Param2.ParameterName = "@driveId";
                get2Command.Parameters.Add(get2Param2);
                var get2Param3 = get2Command.CreateParameter();
                get2Param3.ParameterName = "@driveType";
                get2Command.Parameters.Add(get2Param3);

                get2Param1.Value = identityId.ToByteArray();
                get2Param2.Value = driveId.ToByteArray();
                get2Param3.Value = driveType.ToByteArray();
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader2(rdr,identityId,driveId,driveType);
                        return r;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<DriveDefinitionsRecord>, UnixTimeUtc? nextCursor, long nextRowId)> PagingByCreatedAsync(int count, Guid identityId, UnixTimeUtc? inCursor, long? rowid)
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
            await using var getPaging5Command = cn.CreateCommand();
            {
                getPaging5Command.CommandText = "SELECT rowId,identityId,driveId,driveType,data,created,modified FROM DriveDefinitions " +
                                            "WHERE (identityId = @identityId) AND created <= @created AND rowId < @rowId ORDER BY created DESC , rowId DESC LIMIT @count;";
                var getPaging5Param1 = getPaging5Command.CreateParameter();
                getPaging5Param1.ParameterName = "@created";
                getPaging5Command.Parameters.Add(getPaging5Param1);
                var getPaging5Param2 = getPaging5Command.CreateParameter();
                getPaging5Param2.ParameterName = "@rowId";
                getPaging5Command.Parameters.Add(getPaging5Param2);
                var getPaging5Param3 = getPaging5Command.CreateParameter();
                getPaging5Param3.ParameterName = "@count";
                getPaging5Command.Parameters.Add(getPaging5Param3);
                var getPaging5Param4 = getPaging5Command.CreateParameter();
                getPaging5Param4.ParameterName = "@identityId";
                getPaging5Command.Parameters.Add(getPaging5Param4);

                getPaging5Param1.Value = inCursor?.milliseconds;
                getPaging5Param2.Value = rowid;
                getPaging5Param3.Value = count+1;
                getPaging5Param4.Value = identityId.ToByteArray();

                {
                    await using (var rdr = await getPaging5Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<DriveDefinitionsRecord>();
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
