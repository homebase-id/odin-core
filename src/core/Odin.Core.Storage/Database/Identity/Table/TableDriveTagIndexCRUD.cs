using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class DriveTagIndexRecord
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
        private Guid _tagId;
        public Guid tagId
        {
           get {
                   return _tagId;
               }
           set {
                  _tagId = value;
               }
        }
    } // End of class DriveTagIndexRecord

    public abstract class TableDriveTagIndexCRUD
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableDriveTagIndexCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            {
                if (dropExisting)
                {
                   cmd.CommandText = "DROP TABLE IF EXISTS driveTagIndex;";
                   await cmd.ExecuteNonQueryAsync();
                }
                cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS driveTagIndex("
                 +"identityId BLOB NOT NULL, "
                 +"driveId BLOB NOT NULL, "
                 +"fileId BLOB NOT NULL, "
                 +"tagId BLOB NOT NULL "
                 +", PRIMARY KEY (identityId,driveId,fileId,tagId)"
                 +");"
                 +"CREATE INDEX IF NOT EXISTS Idx0TableDriveTagIndexCRUD ON driveTagIndex(identityId,driveId,fileId);"
                 ;
                 await cmd.ExecuteNonQueryAsync();
            }
        }

        internal virtual async Task<int> InsertAsync(DriveTagIndexRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.tagId.AssertGuidNotEmpty("Guid parameter tagId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO driveTagIndex (identityId,driveId,fileId,tagId) " +
                                             "VALUES (@identityId,@driveId,@fileId,@tagId)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@fileId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@tagId";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.driveId.ToByteArray();
                insertParam3.Value = item.fileId.ToByteArray();
                insertParam4.Value = item.tagId.ToByteArray();
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                }
                return count;
            }
        }

        internal virtual async Task<int> TryInsertAsync(DriveTagIndexRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.tagId.AssertGuidNotEmpty("Guid parameter tagId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT OR IGNORE INTO driveTagIndex (identityId,driveId,fileId,tagId) " +
                                             "VALUES (@identityId,@driveId,@fileId,@tagId)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@fileId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@tagId";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.driveId.ToByteArray();
                insertParam3.Value = item.fileId.ToByteArray();
                insertParam4.Value = item.tagId.ToByteArray();
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                }
                return count;
            }
        }

        internal virtual async Task<int> UpsertAsync(DriveTagIndexRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.tagId.AssertGuidNotEmpty("Guid parameter tagId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO driveTagIndex (identityId,driveId,fileId,tagId) " +
                                             "VALUES (@identityId,@driveId,@fileId,@tagId)"+
                                             "ON CONFLICT (identityId,driveId,fileId,tagId) DO UPDATE "+
                                             "SET  "+
                                             ";";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@driveId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@fileId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@tagId";
                upsertCommand.Parameters.Add(upsertParam4);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.driveId.ToByteArray();
                upsertParam3.Value = item.fileId.ToByteArray();
                upsertParam4.Value = item.tagId.ToByteArray();
                var count = await upsertCommand.ExecuteNonQueryAsync();
                return count;
            }
        }
        internal virtual async Task<int> UpdateAsync(DriveTagIndexRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.tagId.AssertGuidNotEmpty("Guid parameter tagId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE driveTagIndex " +
                                             "SET  "+
                                             "WHERE (identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND tagId = @tagId)";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@driveId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@fileId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@tagId";
                updateCommand.Parameters.Add(updateParam4);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.driveId.ToByteArray();
                updateParam3.Value = item.fileId.ToByteArray();
                updateParam4.Value = item.tagId.ToByteArray();
                var count = await updateCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                }
                return count;
            }
        }

        internal virtual async Task<int> GetCountDirtyAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveTagIndex; PRAGMA read_uncommitted = 0;";
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
            sl.Add("driveId");
            sl.Add("fileId");
            sl.Add("tagId");
            return sl;
        }

        internal virtual async Task<int> GetDriveCountDirtyAsync(Guid driveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountDriveCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountDriveCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveTagIndex WHERE driveId = $driveId;PRAGMA read_uncommitted = 0;";
                var getCountDriveParam1 = getCountDriveCommand.CreateParameter();
                getCountDriveParam1.ParameterName = "$driveId";
                getCountDriveCommand.Parameters.Add(getCountDriveParam1);
                getCountDriveParam1.Value = driveId.ToByteArray();
                var count = await getCountDriveCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            } // using
        }

        // SELECT identityId,driveId,fileId,tagId
        internal DriveTagIndexRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<DriveTagIndexRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveTagIndexRecord();

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
                bytesRead = rdr.GetBytes(1, 0, guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in driveId...");
                item.driveId = new Guid(guid);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in fileId...");
                item.fileId = new Guid(guid);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(3, 0, guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in tagId...");
                item.tagId = new Guid(guid);
            }
            return item;
       }

        internal async Task<int> DeleteAsync(Guid identityId,Guid driveId,Guid fileId,Guid tagId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM driveTagIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND tagId = @tagId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@driveId";
                delete0Command.Parameters.Add(delete0Param2);
                var delete0Param3 = delete0Command.CreateParameter();
                delete0Param3.ParameterName = "@fileId";
                delete0Command.Parameters.Add(delete0Param3);
                var delete0Param4 = delete0Command.CreateParameter();
                delete0Param4.ParameterName = "@tagId";
                delete0Command.Parameters.Add(delete0Param4);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = driveId.ToByteArray();
                delete0Param3.Value = fileId.ToByteArray();
                delete0Param4.Value = tagId.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        internal async Task<int> DeleteAllRowsAsync(Guid identityId,Guid driveId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete1Command = cn.CreateCommand();
            {
                delete1Command.CommandText = "DELETE FROM driveTagIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId";
                var delete1Param1 = delete1Command.CreateParameter();
                delete1Param1.ParameterName = "@identityId";
                delete1Command.Parameters.Add(delete1Param1);
                var delete1Param2 = delete1Command.CreateParameter();
                delete1Param2.ParameterName = "@driveId";
                delete1Command.Parameters.Add(delete1Param2);
                var delete1Param3 = delete1Command.CreateParameter();
                delete1Param3.ParameterName = "@fileId";
                delete1Command.Parameters.Add(delete1Param3);

                delete1Param1.Value = identityId.ToByteArray();
                delete1Param2.Value = driveId.ToByteArray();
                delete1Param3.Value = fileId.ToByteArray();
                var count = await delete1Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        internal DriveTagIndexRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,Guid driveId,Guid fileId,Guid tagId)
        {
            var result = new List<DriveTagIndexRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveTagIndexRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.fileId = fileId;
            item.tagId = tagId;
            return item;
       }

        internal async Task<DriveTagIndexRecord> GetAsync(Guid identityId,Guid driveId,Guid fileId,Guid tagId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT identityId,driveId,fileId,tagId FROM driveTagIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND tagId = @tagId LIMIT 1;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@driveId";
                get0Command.Parameters.Add(get0Param2);
                var get0Param3 = get0Command.CreateParameter();
                get0Param3.ParameterName = "@fileId";
                get0Command.Parameters.Add(get0Param3);
                var get0Param4 = get0Command.CreateParameter();
                get0Param4.ParameterName = "@tagId";
                get0Command.Parameters.Add(get0Param4);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = driveId.ToByteArray();
                get0Param3.Value = fileId.ToByteArray();
                get0Param4.Value = tagId.ToByteArray();
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identityId,driveId,fileId,tagId);
                        return r;
                    } // using
                } //
            } // using
        }

        internal async Task<List<Guid>> GetAsync(Guid identityId,Guid driveId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT tagId FROM driveTagIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId;";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.ParameterName = "@driveId";
                get1Command.Parameters.Add(get1Param2);
                var get1Param3 = get1Command.CreateParameter();
                get1Param3.ParameterName = "@fileId";
                get1Command.Parameters.Add(get1Param3);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = driveId.ToByteArray();
                get1Param3.Value = fileId.ToByteArray();
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        Guid result0tmp;
                        var thelistresult = new List<Guid>();
                        if (!rdr.Read()) {
                            return thelistresult;
                        }
                    byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var guid = new byte[16];
                    while (true)
                    {

                        if (rdr.IsDBNull(0))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            bytesRead = rdr.GetBytes(0, 0, guid, 0, 16);
                            if (bytesRead != 16)
                                throw new Exception("Not a GUID in tagId...");
                            result0tmp = new Guid(guid);
                        }
                        thelistresult.Add(result0tmp);
                        if (!rdr.Read())
                           break;
                    } // while
                    return thelistresult;
                    } // using
                } //
            } // using
        }

    }
}
