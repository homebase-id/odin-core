using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("IdentityDatabase")]

namespace Odin.Core.Storage.SQLite.IdentityDatabase
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

    public class TableDriveTagIndexCRUD : TableBase
    {
        private bool _disposed = false;

        public TableDriveTagIndexCRUD(IdentityDatabase db, CacheHelper cache) : base(db, "driveTagIndex")
        {
        }

        ~TableDriveTagIndexCRUD()
        {
            if (_disposed == false) throw new Exception("TableDriveTagIndexCRUD Not disposed properly");
        }

        public override void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public sealed override void EnsureTableExists(DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = _database.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS driveTagIndex;";
                       conn.ExecuteNonQuery(cmd);
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
                    conn.ExecuteNonQuery(cmd);
            }
        }

        internal virtual int Insert(DatabaseConnection conn, DriveTagIndexRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO driveTagIndex (identityId,driveId,fileId,tagId) " +
                                             "VALUES (@identityId,@driveId,@fileId,@tagId)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@driveId";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@fileId";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@tagId";
                _insertCommand.Parameters.Add(_insertParam4);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.driveId.ToByteArray();
                _insertParam3.Value = item.fileId.ToByteArray();
                _insertParam4.Value = item.tagId.ToByteArray();
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                }
                return count;
            } // Using
        }

        internal virtual int TryInsert(DatabaseConnection conn, DriveTagIndexRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT OR IGNORE INTO driveTagIndex (identityId,driveId,fileId,tagId) " +
                                             "VALUES (@identityId,@driveId,@fileId,@tagId)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@driveId";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@fileId";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@tagId";
                _insertCommand.Parameters.Add(_insertParam4);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.driveId.ToByteArray();
                _insertParam3.Value = item.fileId.ToByteArray();
                _insertParam4.Value = item.tagId.ToByteArray();
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                }
                return count;
            } // Using
        }

        internal virtual int Upsert(DatabaseConnection conn, DriveTagIndexRecord item)
        {
            using (var _upsertCommand = _database.CreateCommand())
            {
                _upsertCommand.CommandText = "INSERT INTO driveTagIndex (identityId,driveId,fileId,tagId) " +
                                             "VALUES (@identityId,@driveId,@fileId,@tagId)"+
                                             "ON CONFLICT (identityId,driveId,fileId,tagId) DO UPDATE "+
                                             "SET  "+
                                             ";";
                var _upsertParam1 = _upsertCommand.CreateParameter();
                _upsertParam1.ParameterName = "@identityId";
                _upsertCommand.Parameters.Add(_upsertParam1);
                var _upsertParam2 = _upsertCommand.CreateParameter();
                _upsertParam2.ParameterName = "@driveId";
                _upsertCommand.Parameters.Add(_upsertParam2);
                var _upsertParam3 = _upsertCommand.CreateParameter();
                _upsertParam3.ParameterName = "@fileId";
                _upsertCommand.Parameters.Add(_upsertParam3);
                var _upsertParam4 = _upsertCommand.CreateParameter();
                _upsertParam4.ParameterName = "@tagId";
                _upsertCommand.Parameters.Add(_upsertParam4);
                _upsertParam1.Value = item.identityId.ToByteArray();
                _upsertParam2.Value = item.driveId.ToByteArray();
                _upsertParam3.Value = item.fileId.ToByteArray();
                _upsertParam4.Value = item.tagId.ToByteArray();
                var count = conn.ExecuteNonQuery(_upsertCommand);
                return count;
            } // Using
        }
        internal virtual int Update(DatabaseConnection conn, DriveTagIndexRecord item)
        {
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText = "UPDATE driveTagIndex " +
                                             "SET  "+
                                             "WHERE (identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND tagId = @tagId)";
                var _updateParam1 = _updateCommand.CreateParameter();
                _updateParam1.ParameterName = "@identityId";
                _updateCommand.Parameters.Add(_updateParam1);
                var _updateParam2 = _updateCommand.CreateParameter();
                _updateParam2.ParameterName = "@driveId";
                _updateCommand.Parameters.Add(_updateParam2);
                var _updateParam3 = _updateCommand.CreateParameter();
                _updateParam3.ParameterName = "@fileId";
                _updateCommand.Parameters.Add(_updateParam3);
                var _updateParam4 = _updateCommand.CreateParameter();
                _updateParam4.ParameterName = "@tagId";
                _updateCommand.Parameters.Add(_updateParam4);
                _updateParam1.Value = item.identityId.ToByteArray();
                _updateParam2.Value = item.driveId.ToByteArray();
                _updateParam3.Value = item.fileId.ToByteArray();
                _updateParam4.Value = item.tagId.ToByteArray();
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                }
                return count;
            } // Using
        }

        internal virtual int GetCountDirty(DatabaseConnection conn)
        {
            using (var _getCountCommand = _database.CreateCommand())
            {
                _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveTagIndex; PRAGMA read_uncommitted = 0;";
                var count = conn.ExecuteScalar(_getCountCommand);
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public override List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("identityId");
            sl.Add("driveId");
            sl.Add("fileId");
            sl.Add("tagId");
            return sl;
        }

        internal virtual int GetDriveCountDirty(DatabaseConnection conn, Guid driveId)
        {
            using (var _getCountDriveCommand = _database.CreateCommand())
            {
                _getCountDriveCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveTagIndex WHERE driveId = $driveId;PRAGMA read_uncommitted = 0;";
                var _getCountDriveParam1 = _getCountDriveCommand.CreateParameter();
                _getCountDriveParam1.ParameterName = "$driveId";
                _getCountDriveCommand.Parameters.Add(_getCountDriveParam1);
                _getCountDriveParam1.Value = driveId.ToByteArray();
                var count = conn.ExecuteScalar(_getCountDriveCommand);
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            } // using
        }

        // SELECT identityId,driveId,fileId,tagId
        internal DriveTagIndexRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<DriveTagIndexRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new DriveTagIndexRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in identityId...");
                item.identityId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in driveId...");
                item.driveId = new Guid(_guid);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in fileId...");
                item.fileId = new Guid(_guid);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(3, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in tagId...");
                item.tagId = new Guid(_guid);
            }
            return item;
       }

        internal int Delete(DatabaseConnection conn, Guid identityId,Guid driveId,Guid fileId,Guid tagId)
        {
            using (var _delete0Command = _database.CreateCommand())
            {
                _delete0Command.CommandText = "DELETE FROM driveTagIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND tagId = @tagId";
                var _delete0Param1 = _delete0Command.CreateParameter();
                _delete0Param1.ParameterName = "@identityId";
                _delete0Command.Parameters.Add(_delete0Param1);
                var _delete0Param2 = _delete0Command.CreateParameter();
                _delete0Param2.ParameterName = "@driveId";
                _delete0Command.Parameters.Add(_delete0Param2);
                var _delete0Param3 = _delete0Command.CreateParameter();
                _delete0Param3.ParameterName = "@fileId";
                _delete0Command.Parameters.Add(_delete0Param3);
                var _delete0Param4 = _delete0Command.CreateParameter();
                _delete0Param4.ParameterName = "@tagId";
                _delete0Command.Parameters.Add(_delete0Param4);

                _delete0Param1.Value = identityId.ToByteArray();
                _delete0Param2.Value = driveId.ToByteArray();
                _delete0Param3.Value = fileId.ToByteArray();
                _delete0Param4.Value = tagId.ToByteArray();
                var count = conn.ExecuteNonQuery(_delete0Command);
                return count;
            } // Using
        }

        internal int DeleteAllRows(DatabaseConnection conn, Guid identityId,Guid driveId,Guid fileId)
        {
            using (var _delete1Command = _database.CreateCommand())
            {
                _delete1Command.CommandText = "DELETE FROM driveTagIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId";
                var _delete1Param1 = _delete1Command.CreateParameter();
                _delete1Param1.ParameterName = "@identityId";
                _delete1Command.Parameters.Add(_delete1Param1);
                var _delete1Param2 = _delete1Command.CreateParameter();
                _delete1Param2.ParameterName = "@driveId";
                _delete1Command.Parameters.Add(_delete1Param2);
                var _delete1Param3 = _delete1Command.CreateParameter();
                _delete1Param3.ParameterName = "@fileId";
                _delete1Command.Parameters.Add(_delete1Param3);

                _delete1Param1.Value = identityId.ToByteArray();
                _delete1Param2.Value = driveId.ToByteArray();
                _delete1Param3.Value = fileId.ToByteArray();
                var count = conn.ExecuteNonQuery(_delete1Command);
                return count;
            } // Using
        }

        internal DriveTagIndexRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid identityId,Guid driveId,Guid fileId,Guid tagId)
        {
            var result = new List<DriveTagIndexRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new DriveTagIndexRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.fileId = fileId;
            item.tagId = tagId;
            return item;
       }

        internal DriveTagIndexRecord Get(DatabaseConnection conn, Guid identityId,Guid driveId,Guid fileId,Guid tagId)
        {
            using (var _get0Command = _database.CreateCommand())
            {
                _get0Command.CommandText = "SELECT identityId,driveId,fileId,tagId FROM driveTagIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND tagId = @tagId LIMIT 1;";
                var _get0Param1 = _get0Command.CreateParameter();
                _get0Param1.ParameterName = "@identityId";
                _get0Command.Parameters.Add(_get0Param1);
                var _get0Param2 = _get0Command.CreateParameter();
                _get0Param2.ParameterName = "@driveId";
                _get0Command.Parameters.Add(_get0Param2);
                var _get0Param3 = _get0Command.CreateParameter();
                _get0Param3.ParameterName = "@fileId";
                _get0Command.Parameters.Add(_get0Param3);
                var _get0Param4 = _get0Command.CreateParameter();
                _get0Param4.ParameterName = "@tagId";
                _get0Command.Parameters.Add(_get0Param4);

                _get0Param1.Value = identityId.ToByteArray();
                _get0Param2.Value = driveId.ToByteArray();
                _get0Param3.Value = fileId.ToByteArray();
                _get0Param4.Value = tagId.ToByteArray();
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identityId,driveId,fileId,tagId);
                        return r;
                    } // using
                } // lock
            } // using
        }

        internal List<Guid> Get(DatabaseConnection conn, Guid identityId,Guid driveId,Guid fileId)
        {
            using (var _get1Command = _database.CreateCommand())
            {
                _get1Command.CommandText = "SELECT tagId FROM driveTagIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId;";
                var _get1Param1 = _get1Command.CreateParameter();
                _get1Param1.ParameterName = "@identityId";
                _get1Command.Parameters.Add(_get1Param1);
                var _get1Param2 = _get1Command.CreateParameter();
                _get1Param2.ParameterName = "@driveId";
                _get1Command.Parameters.Add(_get1Param2);
                var _get1Param3 = _get1Command.CreateParameter();
                _get1Param3.ParameterName = "@fileId";
                _get1Command.Parameters.Add(_get1Param3);

                _get1Param1.Value = identityId.ToByteArray();
                _get1Param2.Value = driveId.ToByteArray();
                _get1Param3.Value = fileId.ToByteArray();
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get1Command, System.Data.CommandBehavior.Default))
                    {
                        Guid result0tmp;
                        var thelistresult = new List<Guid>();
                        if (!rdr.Read()) {
                            return null;
                        }
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                    while (true)
                    {

                        if (rdr.IsDBNull(0))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                            if (bytesRead != 16)
                                throw new Exception("Not a GUID in tagId...");
                            result0tmp = new Guid(_guid);
                        }
                        thelistresult.Add(result0tmp);
                        if (!rdr.Read())
                           break;
                    } // while
                    return thelistresult;
                    } // using
                } // lock
            } // using
        }

    }
}
