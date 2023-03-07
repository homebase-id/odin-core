using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.Sqlite.DriveDatabase
{
    public class AclIndexRecord
    {
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
    } // End of class AclIndexRecord

    public class TableAclIndexCRUD : TableBase
    {
        private bool _disposed = false;
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteParameter _delete0Param2 = null;
        private SqliteCommand _delete1Command = null;
        private static Object _delete1Lock = new Object();
        private SqliteParameter _delete1Param1 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteParameter _get0Param2 = null;
        private SqliteCommand _get1Command = null;
        private static Object _get1Lock = new Object();
        private SqliteParameter _get1Param1 = null;

        public TableAclIndexCRUD(DriveDatabase db) : base(db)
        {
        }

        ~TableAclIndexCRUD()
        {
            if (_disposed == false) throw new Exception("TableAclIndexCRUD Not disposed properly");
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
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS aclIndex;";
                    cmd.ExecuteNonQuery(_database);
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS aclIndex("
                     +"fileId BLOB NOT NULL, "
                     +"aclMemberId BLOB NOT NULL "
                     +", PRIMARY KEY (fileId,aclMemberId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableAclIndexCRUD ON aclIndex(aclMemberId);"
                     ;
                cmd.ExecuteNonQuery(_database);
            }
        }

        public virtual int Insert(AclIndexRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO aclIndex (fileId,aclMemberId) " +
                                                 "VALUES ($fileId,$aclMemberId)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$fileId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$aclMemberId";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.fileId.ToByteArray();
                _insertParam2.Value = item.aclMemberId.ToByteArray();
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public virtual int Upsert(AclIndexRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO aclIndex (fileId,aclMemberId) " +
                                                 "VALUES ($fileId,$aclMemberId)"+
                                                 "ON CONFLICT (fileId,aclMemberId) DO UPDATE "+
                                                 "SET ;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$fileId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$aclMemberId";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.fileId.ToByteArray();
                _upsertParam2.Value = item.aclMemberId.ToByteArray();
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public virtual int Update(AclIndexRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE aclIndex " +
                                                 "SET  "+
                                                 "WHERE (fileId = $fileId,aclMemberId = $aclMemberId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$fileId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$aclMemberId";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.fileId.ToByteArray();
                _updateParam2.Value = item.aclMemberId.ToByteArray();
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public int Delete(Guid fileId,Guid aclMemberId)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM aclIndex " +
                                                 "WHERE fileId = $fileId AND aclMemberId = $aclMemberId";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$fileId";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$aclMemberId";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = fileId.ToByteArray();
                _delete0Param2.Value = aclMemberId.ToByteArray();
                _database.BeginTransaction();
                return _delete0Command.ExecuteNonQuery(_database);
            } // Lock
        }

        public int DeleteAllRows(Guid fileId)
        {
            lock (_delete1Lock)
            {
                if (_delete1Command == null)
                {
                    _delete1Command = _database.CreateCommand();
                    _delete1Command.CommandText = "DELETE FROM aclIndex " +
                                                 "WHERE fileId = $fileId";
                    _delete1Param1 = _delete1Command.CreateParameter();
                    _delete1Command.Parameters.Add(_delete1Param1);
                    _delete1Param1.ParameterName = "$fileId";
                    _delete1Command.Prepare();
                }
                _delete1Param1.Value = fileId.ToByteArray();
                _database.BeginTransaction();
                return _delete1Command.ExecuteNonQuery(_database);
            } // Lock
        }

        public AclIndexRecord Get(Guid fileId,Guid aclMemberId)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT  FROM aclIndex " +
                                                 "WHERE fileId = $fileId AND aclMemberId = $aclMemberId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$fileId";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$aclMemberId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = fileId.ToByteArray();
                _get0Param2.Value = aclMemberId.ToByteArray();
                using (SqliteDataReader rdr = _get0Command.ExecuteReader(System.Data.CommandBehavior.SingleRow, _database))
                {
                    var result = new AclIndexRecord();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                        var item = new AclIndexRecord();
                        item.fileId = fileId;
                        item.aclMemberId = aclMemberId;
                    return item;
                } // using
            } // lock
        }

        public List<Guid> Get(Guid fileId)
        {
            lock (_get1Lock)
            {
                if (_get1Command == null)
                {
                    _get1Command = _database.CreateCommand();
                    _get1Command.CommandText = "SELECT aclMemberId FROM aclIndex " +
                                                 "WHERE fileId = $fileId;";
                    _get1Param1 = _get1Command.CreateParameter();
                    _get1Command.Parameters.Add(_get1Param1);
                    _get1Param1.ParameterName = "$fileId";
                    _get1Command.Prepare();
                }
                _get1Param1.Value = fileId.ToByteArray();
                using (SqliteDataReader rdr = _get1Command.ExecuteReader(System.Data.CommandBehavior.Default, _database))
                {
                    var result0 = new List<Guid>();
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
                                throw new Exception("Not a GUID in aclMemberId...");
                            result0.Add(new Guid(_guid));
                        }
                        if (!rdr.Read())
                           break;
                    } // while
                    return result0;
                } // using
            } // lock
        }

    }
}
