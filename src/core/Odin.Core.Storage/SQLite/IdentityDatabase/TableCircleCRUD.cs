using System;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class CircleRecord
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
        private string _circleName;
        public string circleName
        {
           get {
                   return _circleName;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 2) throw new Exception("Too short");
                    if (value?.Length > 80) throw new Exception("Too long");
                  _circleName = value;
               }
        }
        private Guid _circleId;
        public Guid circleId
        {
           get {
                   return _circleId;
               }
           set {
                  _circleId = value;
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
                    if (value?.Length > 65000) throw new Exception("Too long");
                  _data = value;
               }
        }
    } // End of class CircleRecord

    public class TableCircleCRUD
    {
        private readonly CacheHelper _cache;

        public TableCircleCRUD(CacheHelper cache)
        {
            _cache = cache;
        }


        public async Task EnsureTableExistsAsync(DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = conn.db.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS circle;";
                       await conn.ExecuteNonQueryAsync(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS circle("
                     +"identityId BLOB NOT NULL, "
                     +"circleName STRING NOT NULL, "
                     +"circleId BLOB NOT NULL UNIQUE, "
                     +"data BLOB  "
                     +", PRIMARY KEY (identityId,circleId)"
                     +");"
                     ;
                    await conn.ExecuteNonQueryAsync(cmd);
            }
        }

        internal virtual async Task<int> InsertAsync(DatabaseConnection conn, CircleRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.circleId, "Guid parameter circleId cannot be set to Empty GUID.");
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT INTO circle (identityId,circleName,circleId,data) " +
                                             "VALUES (@identityId,@circleName,@circleId,@data)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@circleName";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@circleId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.circleName;
                insertParam3.Value = item.circleId.ToByteArray();
                insertParam4.Value = item.data ?? (object)DBNull.Value;
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableCircleCRUD", item.identityId.ToString()+item.circleId.ToString(), item);
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> TryInsertAsync(DatabaseConnection conn, CircleRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.circleId, "Guid parameter circleId cannot be set to Empty GUID.");
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT OR IGNORE INTO circle (identityId,circleName,circleId,data) " +
                                             "VALUES (@identityId,@circleName,@circleId,@data)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@circleName";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@circleId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.circleName;
                insertParam3.Value = item.circleId.ToByteArray();
                insertParam4.Value = item.data ?? (object)DBNull.Value;
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableCircleCRUD", item.identityId.ToString()+item.circleId.ToString(), item);
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> UpsertAsync(DatabaseConnection conn, CircleRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.circleId, "Guid parameter circleId cannot be set to Empty GUID.");
            using (var upsertCommand = conn.db.CreateCommand())
            {
                upsertCommand.CommandText = "INSERT INTO circle (identityId,circleName,circleId,data) " +
                                             "VALUES (@identityId,@circleName,@circleId,@data)"+
                                             "ON CONFLICT (identityId,circleId) DO UPDATE "+
                                             "SET circleName = @circleName,data = @data "+
                                             ";";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@circleName";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@circleId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@data";
                upsertCommand.Parameters.Add(upsertParam4);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.circleName;
                upsertParam3.Value = item.circleId.ToByteArray();
                upsertParam4.Value = item.data ?? (object)DBNull.Value;
                var count = await conn.ExecuteNonQueryAsync(upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableCircleCRUD", item.identityId.ToString()+item.circleId.ToString(), item);
                return count;
            } // Using
        }
        internal virtual async Task<int> UpdateAsync(DatabaseConnection conn, CircleRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.circleId, "Guid parameter circleId cannot be set to Empty GUID.");
            using (var updateCommand = conn.db.CreateCommand())
            {
                updateCommand.CommandText = "UPDATE circle " +
                                             "SET circleName = @circleName,data = @data "+
                                             "WHERE (identityId = @identityId AND circleId = @circleId)";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@circleName";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@circleId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@data";
                updateCommand.Parameters.Add(updateParam4);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.circleName;
                updateParam3.Value = item.circleId.ToByteArray();
                updateParam4.Value = item.data ?? (object)DBNull.Value;
                var count = await conn.ExecuteNonQueryAsync(updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableCircleCRUD", item.identityId.ToString()+item.circleId.ToString(), item);
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> GetCountDirtyAsync(DatabaseConnection conn)
        {
            using (var getCountCommand = conn.db.CreateCommand())
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM circle; PRAGMA read_uncommitted = 0;";
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
            sl.Add("identityId");
            sl.Add("circleName");
            sl.Add("circleId");
            sl.Add("data");
            return sl;
        }

        // SELECT identityId,circleName,circleId,data
        internal CircleRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<CircleRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in identityId...");
                item.identityId = new Guid(guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.circleName = rdr.GetString(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in circleId...");
                item.circleId = new Guid(guid);
            }

            if (rdr.IsDBNull(3))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(3, 0, tmpbuf, 0, 65000+1);
                if (bytesRead > 65000)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        internal async Task<int> DeleteAsync(DatabaseConnection conn, Guid identityId,Guid circleId)
        {
            using (var delete0Command = conn.db.CreateCommand())
            {
                delete0Command.CommandText = "DELETE FROM circle " +
                                             "WHERE identityId = @identityId AND circleId = @circleId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@circleId";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = circleId.ToByteArray();
                var count = await conn.ExecuteNonQueryAsync(delete0Command);
                if (count > 0)
                    _cache.Remove("TableCircleCRUD", identityId.ToString()+circleId.ToString());
                return count;
            } // Using
        }

        internal CircleRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,Guid circleId)
        {
            var result = new List<CircleRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleRecord();
            item.identityId = identityId;
            item.circleId = circleId;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.circleName = rdr.GetString(0);
            }

            if (rdr.IsDBNull(1))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(1, 0, tmpbuf, 0, 65000+1);
                if (bytesRead > 65000)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        internal async Task<CircleRecord> GetAsync(DatabaseConnection conn, Guid identityId,Guid circleId)
        {
            var (hit, cacheObject) = _cache.Get("TableCircleCRUD", identityId.ToString()+circleId.ToString());
            if (hit)
                return (CircleRecord)cacheObject;
            using (var get0Command = conn.db.CreateCommand())
            {
                get0Command.CommandText = "SELECT circleName,data FROM circle " +
                                             "WHERE identityId = @identityId AND circleId = @circleId LIMIT 1;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@circleId";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = circleId.ToByteArray();
                {
                    using (var rdr = await conn.ExecuteReaderAsync(get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableCircleCRUD", identityId.ToString()+circleId.ToString(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identityId,circleId);
                        _cache.AddOrUpdate("TableCircleCRUD", identityId.ToString()+circleId.ToString(), r);
                        return r;
                    } // using
                } //
            } // using
        }

        internal async Task<(List<CircleRecord>, Guid? nextCursor)> PagingByCircleIdAsync(DatabaseConnection conn, int count, Guid identityId, Guid? inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = Guid.Empty;

            using (var getPaging3Command = conn.db.CreateCommand())
            {
                getPaging3Command.CommandText = "SELECT identityId,circleName,circleId,data FROM circle " +
                                            "WHERE (identityId = @identityId) AND circleId > @circleId ORDER BY circleId ASC LIMIT $_count;";
                var getPaging3Param1 = getPaging3Command.CreateParameter();
                getPaging3Param1.ParameterName = "@circleId";
                getPaging3Command.Parameters.Add(getPaging3Param1);
                var getPaging3Param2 = getPaging3Command.CreateParameter();
                getPaging3Param2.ParameterName = "$_count";
                getPaging3Command.Parameters.Add(getPaging3Param2);
                var getPaging3Param3 = getPaging3Command.CreateParameter();
                getPaging3Param3.ParameterName = "@identityId";
                getPaging3Command.Parameters.Add(getPaging3Param3);

                getPaging3Param1.Value = inCursor?.ToByteArray();
                getPaging3Param2.Value = count+1;
                getPaging3Param3.Value = identityId.ToByteArray();

                {
                    using (var rdr = await conn.ExecuteReaderAsync(getPaging3Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<CircleRecord>();
                        Guid? nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].circleId;
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

    }
}
