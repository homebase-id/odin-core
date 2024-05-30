using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class DriveAclIndexRecord
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
        private Guid _aclMemberId;
        public Guid aclMemberId
        {
           get {
                   return _aclMemberId;
               }
           set {
                  _aclMemberId = value;
               }
        }
    } // End of class DriveAclIndexRecord

    public class TableDriveAclIndexCRUD : TableBase
    {
        private bool _disposed = false;

        public TableDriveAclIndexCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
        {
        }

        ~TableDriveAclIndexCRUD()
        {
            if (_disposed == false) throw new Exception("TableDriveAclIndexCRUD Not disposed properly");
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
                       cmd.CommandText = "DROP TABLE IF EXISTS driveAclIndex;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS driveAclIndex("
                     +"driveId BLOB NOT NULL, "
                     +"fileId BLOB NOT NULL, "
                     +"aclMemberId BLOB NOT NULL "
                     +", PRIMARY KEY (driveId,fileId,aclMemberId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableDriveAclIndexCRUD ON driveAclIndex(driveId,aclMemberId);"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        public virtual int Insert(DatabaseConnection conn, DriveAclIndexRecord item)
        {
                using (var _insertCommand = _database.CreateCommand())
                {
                    _insertCommand.CommandText = "INSERT INTO driveAclIndex (driveId,fileId,aclMemberId) " +
                                                 "VALUES ($driveId,$fileId,$aclMemberId)";
                    var _insertParam1 = _insertCommand.CreateParameter();
                    _insertParam1.ParameterName = "$driveId";
                    _insertCommand.Parameters.Add(_insertParam1);
                    var _insertParam2 = _insertCommand.CreateParameter();
                    _insertParam2.ParameterName = "$fileId";
                    _insertCommand.Parameters.Add(_insertParam2);
                    var _insertParam3 = _insertCommand.CreateParameter();
                    _insertParam3.ParameterName = "$aclMemberId";
                    _insertCommand.Parameters.Add(_insertParam3);
                _insertParam1.Value = item.driveId.ToByteArray();
                _insertParam2.Value = item.fileId.ToByteArray();
                _insertParam3.Value = item.aclMemberId.ToByteArray();
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                 {
                 }
                return count;
                } // Using
        }

        public virtual int Upsert(DatabaseConnection conn, DriveAclIndexRecord item)
        {
                using (var _upsertCommand = _database.CreateCommand())
                {
                    _upsertCommand.CommandText = "INSERT INTO driveAclIndex (driveId,fileId,aclMemberId) " +
                                                 "VALUES ($driveId,$fileId,$aclMemberId)"+
                                                 "ON CONFLICT (driveId,fileId,aclMemberId) DO UPDATE "+
                                                 "SET  "+
                                                 ";";
                    var _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertParam1.ParameterName = "$driveId";
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    var _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertParam2.ParameterName = "$fileId";
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    var _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertParam3.ParameterName = "$aclMemberId";
                    _upsertCommand.Parameters.Add(_upsertParam3);
                _upsertParam1.Value = item.driveId.ToByteArray();
                _upsertParam2.Value = item.fileId.ToByteArray();
                _upsertParam3.Value = item.aclMemberId.ToByteArray();
                var count = conn.ExecuteNonQuery(_upsertCommand);
                return count;
                } // Using
        }
        public virtual int Update(DatabaseConnection conn, DriveAclIndexRecord item)
        {
                using (var _updateCommand = _database.CreateCommand())
                {
                    _updateCommand.CommandText = "UPDATE driveAclIndex " +
                                                 "SET  "+
                                                 "WHERE (driveId = $driveId AND fileId = $fileId AND aclMemberId = $aclMemberId)";
                    var _updateParam1 = _updateCommand.CreateParameter();
                    _updateParam1.ParameterName = "$driveId";
                    _updateCommand.Parameters.Add(_updateParam1);
                    var _updateParam2 = _updateCommand.CreateParameter();
                    _updateParam2.ParameterName = "$fileId";
                    _updateCommand.Parameters.Add(_updateParam2);
                    var _updateParam3 = _updateCommand.CreateParameter();
                    _updateParam3.ParameterName = "$aclMemberId";
                    _updateCommand.Parameters.Add(_updateParam3);
                _updateParam1.Value = item.driveId.ToByteArray();
                _updateParam2.Value = item.fileId.ToByteArray();
                _updateParam3.Value = item.aclMemberId.ToByteArray();
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                }
                return count;
                } // Using
        }

        public virtual int GetCountDirty(DatabaseConnection conn)
        {
                using (var _getCountCommand = _database.CreateCommand())
                {
                    _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveAclIndex; PRAGMA read_uncommitted = 0;";
                    var count = conn.ExecuteNonQuery(_getCountCommand);
                    return count;
                }
        }

        public virtual int GetDriveCountDirty(DatabaseConnection conn, Guid driveId)
        {
                using (var _getCountDriveCommand = _database.CreateCommand())
                {
                    _getCountDriveCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveAclIndex WHERE driveId = $driveId;PRAGMA read_uncommitted = 0;";
                    var _getCountDriveParam1 = _getCountDriveCommand.CreateParameter();
                    _getCountDriveParam1.ParameterName = "$driveId";
                    _getCountDriveCommand.Parameters.Add(_getCountDriveParam1);
                    _getCountDriveParam1.Value = driveId.ToByteArray();
                    var count = conn.ExecuteNonQuery(_getCountDriveCommand);
                    return count;
                } // using
        }

        // SELECT driveId,fileId,aclMemberId
        public DriveAclIndexRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<DriveAclIndexRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new DriveAclIndexRecord();

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
                    throw new Exception("Not a GUID in aclMemberId...");
                item.aclMemberId = new Guid(_guid);
            }
            return item;
       }

        public int Delete(DatabaseConnection conn, Guid driveId,Guid fileId,Guid aclMemberId)
        {
                using (var _delete0Command = _database.CreateCommand())
                {
                    _delete0Command.CommandText = "DELETE FROM driveAclIndex " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId AND aclMemberId = $aclMemberId";
                    var _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Param1.ParameterName = "$driveId";
                    _delete0Command.Parameters.Add(_delete0Param1);
                    var _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Param2.ParameterName = "$fileId";
                    _delete0Command.Parameters.Add(_delete0Param2);
                    var _delete0Param3 = _delete0Command.CreateParameter();
                    _delete0Param3.ParameterName = "$aclMemberId";
                    _delete0Command.Parameters.Add(_delete0Param3);

                _delete0Param1.Value = driveId.ToByteArray();
                _delete0Param2.Value = fileId.ToByteArray();
                _delete0Param3.Value = aclMemberId.ToByteArray();
                var count = conn.ExecuteNonQuery(_delete0Command);
                return count;
                } // Using
        }

        public int DeleteAllRows(DatabaseConnection conn, Guid driveId,Guid fileId)
        {
                using (var _delete1Command = _database.CreateCommand())
                {
                    _delete1Command.CommandText = "DELETE FROM driveAclIndex " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId";
                    var _delete1Param1 = _delete1Command.CreateParameter();
                    _delete1Param1.ParameterName = "$driveId";
                    _delete1Command.Parameters.Add(_delete1Param1);
                    var _delete1Param2 = _delete1Command.CreateParameter();
                    _delete1Param2.ParameterName = "$fileId";
                    _delete1Command.Parameters.Add(_delete1Param2);

                _delete1Param1.Value = driveId.ToByteArray();
                _delete1Param2.Value = fileId.ToByteArray();
                var count = conn.ExecuteNonQuery(_delete1Command);
                return count;
                } // Using
        }

        public DriveAclIndexRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid driveId,Guid fileId,Guid aclMemberId)
        {
            var result = new List<DriveAclIndexRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new DriveAclIndexRecord();
            item.driveId = driveId;
            item.fileId = fileId;
            item.aclMemberId = aclMemberId;
            return item;
       }

        public DriveAclIndexRecord Get(DatabaseConnection conn, Guid driveId,Guid fileId,Guid aclMemberId)
        {
                using (var _get0Command = _database.CreateCommand())
                {
                    _get0Command.CommandText = "SELECT driveId,fileId,aclMemberId FROM driveAclIndex " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId AND aclMemberId = $aclMemberId LIMIT 1;";
                    var _get0Param1 = _get0Command.CreateParameter();
                    _get0Param1.ParameterName = "$driveId";
                    _get0Command.Parameters.Add(_get0Param1);
                    var _get0Param2 = _get0Command.CreateParameter();
                    _get0Param2.ParameterName = "$fileId";
                    _get0Command.Parameters.Add(_get0Param2);
                    var _get0Param3 = _get0Command.CreateParameter();
                    _get0Param3.ParameterName = "$aclMemberId";
                    _get0Command.Parameters.Add(_get0Param3);

                _get0Param1.Value = driveId.ToByteArray();
                _get0Param2.Value = fileId.ToByteArray();
                _get0Param3.Value = aclMemberId.ToByteArray();
                    lock (conn._lock)
                    {
                using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, driveId,fileId,aclMemberId);
                    return r;
                } // using
            } // lock
            } // using
        }

        public List<Guid> Get(DatabaseConnection conn, Guid driveId,Guid fileId)
        {
                using (var _get1Command = _database.CreateCommand())
                {
                    _get1Command.CommandText = "SELECT aclMemberId FROM driveAclIndex " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId;";
                    var _get1Param1 = _get1Command.CreateParameter();
                    _get1Param1.ParameterName = "$driveId";
                    _get1Command.Parameters.Add(_get1Param1);
                    var _get1Param2 = _get1Command.CreateParameter();
                    _get1Param2.ParameterName = "$fileId";
                    _get1Command.Parameters.Add(_get1Param2);

                _get1Param1.Value = driveId.ToByteArray();
                _get1Param2.Value = fileId.ToByteArray();
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
                                throw new Exception("Not a GUID in aclMemberId...");
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
