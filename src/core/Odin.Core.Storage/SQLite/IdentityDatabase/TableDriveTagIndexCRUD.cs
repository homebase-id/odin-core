using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class DriveTagIndexRecord
    {
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
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteParameter _insertParam3 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteParameter _updateParam3 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteParameter _upsertParam3 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteParameter _delete0Param2 = null;
        private SqliteParameter _delete0Param3 = null;
        private SqliteCommand _delete1Command = null;
        private static Object _delete1Lock = new Object();
        private SqliteParameter _delete1Param1 = null;
        private SqliteParameter _delete1Param2 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteParameter _get0Param2 = null;
        private SqliteParameter _get0Param3 = null;
        private SqliteCommand _get1Command = null;
        private static Object _get1Lock = new Object();
        private SqliteParameter _get1Param1 = null;
        private SqliteParameter _get1Param2 = null;

        public TableDriveTagIndexCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
        {
        }

        ~TableDriveTagIndexCRUD()
        {
            if (_disposed == false) throw new Exception("TableDriveTagIndexCRUD Not disposed properly");
        }

        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;
            _updateCommand?.Dispose();
            _updateCommand = null;
            _upsertCommand?.Dispose();
            _upsertCommand = null;
            _delete0Command?.Dispose();
            _delete0Command = null;
            _delete1Command?.Dispose();
            _delete1Command = null;
            _get0Command?.Dispose();
            _get0Command = null;
            _get1Command?.Dispose();
            _get1Command = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public sealed override void EnsureTableExists(DatabaseBase.DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = _database.CreateCommand(conn))
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS driveTagIndex;";
                        _database.ExecuteNonQuery(conn, cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS driveTagIndex("
                     +"driveId BLOB NOT NULL, "
                     +"fileId BLOB NOT NULL, "
                     +"tagId BLOB NOT NULL "
                     +", PRIMARY KEY (driveId,fileId,tagId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableDriveTagIndexCRUD ON driveTagIndex(driveId,fileId);"
                     ;
                    _database.ExecuteNonQuery(conn, cmd);
            }
        }

        public virtual int Insert(DatabaseBase.DatabaseConnection conn, DriveTagIndexRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand(conn);
                    _insertCommand.CommandText = "INSERT INTO driveTagIndex (driveId,fileId,tagId) " +
                                                 "VALUES ($driveId,$fileId,$tagId)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$driveId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$fileId";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$tagId";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.driveId.ToByteArray();
                _insertParam2.Value = item.fileId.ToByteArray();
                _insertParam3.Value = item.tagId.ToByteArray();
                var count = _database.ExecuteNonQuery(conn, _insertCommand);
                if (count > 0)
                 {
                 }
                return count;
            } // Lock
        }

        public virtual int Upsert(DatabaseBase.DatabaseConnection conn, DriveTagIndexRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand(conn);
                    _upsertCommand.CommandText = "INSERT INTO driveTagIndex (driveId,fileId,tagId) " +
                                                 "VALUES ($driveId,$fileId,$tagId)"+
                                                 "ON CONFLICT (driveId,fileId,tagId) DO UPDATE "+
                                                 "SET  "+
                                                 ";";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$driveId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$fileId";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$tagId";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.driveId.ToByteArray();
                _upsertParam2.Value = item.fileId.ToByteArray();
                _upsertParam3.Value = item.tagId.ToByteArray();
                var count = _database.ExecuteNonQuery(conn, _upsertCommand);
                return count;
            } // Lock
        }
        public virtual int Update(DatabaseBase.DatabaseConnection conn, DriveTagIndexRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand(conn);
                    _updateCommand.CommandText = "UPDATE driveTagIndex " +
                                                 "SET  "+
                                                 "WHERE (driveId = $driveId,fileId = $fileId,tagId = $tagId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$driveId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$fileId";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$tagId";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.driveId.ToByteArray();
                _updateParam2.Value = item.fileId.ToByteArray();
                _updateParam3.Value = item.tagId.ToByteArray();
                var count = _database.ExecuteNonQuery(conn, _updateCommand);
                if (count > 0)
                {
                }
                return count;
            } // Lock
        }

        // SELECT driveId,fileId,tagId
        public DriveTagIndexRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
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
                    throw new Exception("Not a GUID in driveId...");
                item.driveId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in fileId...");
                item.fileId = new Guid(_guid);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in tagId...");
                item.tagId = new Guid(_guid);
            }
            return item;
       }

        public int Delete(DatabaseBase.DatabaseConnection conn, Guid driveId,Guid fileId,Guid tagId)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand(conn);
                    _delete0Command.CommandText = "DELETE FROM driveTagIndex " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId AND tagId = $tagId";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$driveId";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$fileId";
                    _delete0Param3 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param3);
                    _delete0Param3.ParameterName = "$tagId";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = driveId.ToByteArray();
                _delete0Param2.Value = fileId.ToByteArray();
                _delete0Param3.Value = tagId.ToByteArray();
                var count = _database.ExecuteNonQuery(conn, _delete0Command);
                return count;
            } // Lock
        }

        public int DeleteAllRows(DatabaseBase.DatabaseConnection conn, Guid driveId,Guid fileId)
        {
            lock (_delete1Lock)
            {
                if (_delete1Command == null)
                {
                    _delete1Command = _database.CreateCommand(conn);
                    _delete1Command.CommandText = "DELETE FROM driveTagIndex " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId";
                    _delete1Param1 = _delete1Command.CreateParameter();
                    _delete1Command.Parameters.Add(_delete1Param1);
                    _delete1Param1.ParameterName = "$driveId";
                    _delete1Param2 = _delete1Command.CreateParameter();
                    _delete1Command.Parameters.Add(_delete1Param2);
                    _delete1Param2.ParameterName = "$fileId";
                    _delete1Command.Prepare();
                }
                _delete1Param1.Value = driveId.ToByteArray();
                _delete1Param2.Value = fileId.ToByteArray();
                var count = _database.ExecuteNonQuery(conn, _delete1Command);
                return count;
            } // Lock
        }

        public DriveTagIndexRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid driveId,Guid fileId,Guid tagId)
        {
            var result = new List<DriveTagIndexRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new DriveTagIndexRecord();
            item.driveId = driveId;
            item.fileId = fileId;
            item.tagId = tagId;
            return item;
       }

        public DriveTagIndexRecord Get(DatabaseBase.DatabaseConnection conn, Guid driveId,Guid fileId,Guid tagId)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand(conn);
                    _get0Command.CommandText = "SELECT driveId,fileId,tagId FROM driveTagIndex " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId AND tagId = $tagId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$driveId";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$fileId";
                    _get0Param3 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param3);
                    _get0Param3.ParameterName = "$tagId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = driveId.ToByteArray();
                _get0Param2.Value = fileId.ToByteArray();
                _get0Param3.Value = tagId.ToByteArray();
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, driveId,fileId,tagId);
                    return r;
                } // using
            } // lock
        }

        public List<Guid> Get(DatabaseBase.DatabaseConnection conn, Guid driveId,Guid fileId)
        {
            lock (_get1Lock)
            {
                if (_get1Command == null)
                {
                    _get1Command = _database.CreateCommand(conn);
                    _get1Command.CommandText = "SELECT tagId FROM driveTagIndex " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId;";
                    _get1Param1 = _get1Command.CreateParameter();
                    _get1Command.Parameters.Add(_get1Param1);
                    _get1Param1.ParameterName = "$driveId";
                    _get1Param2 = _get1Command.CreateParameter();
                    _get1Command.Parameters.Add(_get1Param2);
                    _get1Param2.ParameterName = "$fileId";
                    _get1Command.Prepare();
                }
                _get1Param1.Value = driveId.ToByteArray();
                _get1Param2.Value = fileId.ToByteArray();
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get1Command, System.Data.CommandBehavior.Default))
                {
                    Guid result0tmp;
                    var thelistresult = new List<Guid>();
                    if (!rdr.Read())
                        return null;
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
        }

    }
}
