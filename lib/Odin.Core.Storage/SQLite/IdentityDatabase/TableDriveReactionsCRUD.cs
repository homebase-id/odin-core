using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class DriveReactionsRecord
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
        private Guid _postId;
        public Guid postId
        {
           get {
                   return _postId;
               }
           set {
                  _postId = value;
               }
        }
        private string _singleReaction;
        public string singleReaction
        {
           get {
                   return _singleReaction;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 3) throw new Exception("Too short");
                    if (value?.Length > 80) throw new Exception("Too long");
                  _singleReaction = value;
               }
        }
    } // End of class DriveReactionsRecord

    public class TableDriveReactionsCRUD : TableBase
    {
        private bool _disposed = false;
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteParameter _insertParam3 = null;
        private SqliteParameter _insertParam4 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteParameter _updateParam3 = null;
        private SqliteParameter _updateParam4 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteParameter _upsertParam3 = null;
        private SqliteParameter _upsertParam4 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteParameter _delete0Param2 = null;
        private SqliteParameter _delete0Param3 = null;
        private SqliteParameter _delete0Param4 = null;
        private SqliteCommand _delete1Command = null;
        private static Object _delete1Lock = new Object();
        private SqliteParameter _delete1Param1 = null;
        private SqliteParameter _delete1Param2 = null;
        private SqliteParameter _delete1Param3 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteParameter _get0Param2 = null;
        private SqliteParameter _get0Param3 = null;
        private SqliteParameter _get0Param4 = null;

        public TableDriveReactionsCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
        {
        }

        ~TableDriveReactionsCRUD()
        {
            if (_disposed == false) throw new Exception("TableDriveReactionsCRUD Not disposed properly");
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
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS driveReactions;";
                    _database.ExecuteNonQuery(cmd);
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS driveReactions("
                     +"driveId BLOB NOT NULL, "
                     +"identity BLOB NOT NULL, "
                     +"postId BLOB NOT NULL, "
                     +"singleReaction STRING NOT NULL "
                     +", PRIMARY KEY (driveId,identity,postId,singleReaction)"
                     +");"
                     ;
                _database.ExecuteNonQuery(cmd);
                _database.Commit();
            }
        }

        public virtual int Insert(DriveReactionsRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO driveReactions (driveId,identity,postId,singleReaction) " +
                                                 "VALUES ($driveId,$identity,$postId,$singleReaction)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$driveId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$identity";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$postId";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$singleReaction";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.driveId.ToByteArray();
                _insertParam2.Value = item.identity.DomainName;
                _insertParam3.Value = item.postId.ToByteArray();
                _insertParam4.Value = item.singleReaction;
                var count = _database.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                 {
                 }
                return count;
            } // Lock
        }

        public virtual int Upsert(DriveReactionsRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO driveReactions (driveId,identity,postId,singleReaction) " +
                                                 "VALUES ($driveId,$identity,$postId,$singleReaction)"+
                                                 "ON CONFLICT (driveId,identity,postId,singleReaction) DO UPDATE "+
                                                 "SET  "+
                                                 ";";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$driveId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$identity";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$postId";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$singleReaction";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.driveId.ToByteArray();
                _upsertParam2.Value = item.identity.DomainName;
                _upsertParam3.Value = item.postId.ToByteArray();
                _upsertParam4.Value = item.singleReaction;
                var count = _database.ExecuteNonQuery(_upsertCommand);
                return count;
            } // Lock
        }
        public virtual int Update(DriveReactionsRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE driveReactions " +
                                                 "SET  "+
                                                 "WHERE (driveId = $driveId,identity = $identity,postId = $postId,singleReaction = $singleReaction)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$driveId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$identity";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$postId";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$singleReaction";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.driveId.ToByteArray();
                _updateParam2.Value = item.identity.DomainName;
                _updateParam3.Value = item.postId.ToByteArray();
                _updateParam4.Value = item.singleReaction;
                var count = _database.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                }
                return count;
            } // Lock
        }

        // SELECT driveId,identity,postId,singleReaction
        public DriveReactionsRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<DriveReactionsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new DriveReactionsRecord();

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
                item.identity = new OdinId(rdr.GetString(1));
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in postId...");
                item.postId = new Guid(_guid);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.singleReaction = rdr.GetString(3);
            }
            return item;
       }

        public int Delete(Guid driveId,OdinId identity,Guid postId,string singleReaction)
        {
            if (singleReaction == null) throw new Exception("Cannot be null");
            if (singleReaction?.Length < 3) throw new Exception("Too short");
            if (singleReaction?.Length > 80) throw new Exception("Too long");
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM driveReactions " +
                                                 "WHERE driveId = $driveId AND identity = $identity AND postId = $postId AND singleReaction = $singleReaction";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$driveId";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$identity";
                    _delete0Param3 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param3);
                    _delete0Param3.ParameterName = "$postId";
                    _delete0Param4 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param4);
                    _delete0Param4.ParameterName = "$singleReaction";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = driveId.ToByteArray();
                _delete0Param2.Value = identity.DomainName;
                _delete0Param3.Value = postId.ToByteArray();
                _delete0Param4.Value = singleReaction;
                var count = _database.ExecuteNonQuery(_delete0Command);
                return count;
            } // Lock
        }

        public int DeleteAllReactions(Guid driveId,OdinId identity,Guid postId)
        {
            lock (_delete1Lock)
            {
                if (_delete1Command == null)
                {
                    _delete1Command = _database.CreateCommand();
                    _delete1Command.CommandText = "DELETE FROM driveReactions " +
                                                 "WHERE driveId = $driveId AND identity = $identity AND postId = $postId";
                    _delete1Param1 = _delete1Command.CreateParameter();
                    _delete1Command.Parameters.Add(_delete1Param1);
                    _delete1Param1.ParameterName = "$driveId";
                    _delete1Param2 = _delete1Command.CreateParameter();
                    _delete1Command.Parameters.Add(_delete1Param2);
                    _delete1Param2.ParameterName = "$identity";
                    _delete1Param3 = _delete1Command.CreateParameter();
                    _delete1Command.Parameters.Add(_delete1Param3);
                    _delete1Param3.ParameterName = "$postId";
                    _delete1Command.Prepare();
                }
                _delete1Param1.Value = driveId.ToByteArray();
                _delete1Param2.Value = identity.DomainName;
                _delete1Param3.Value = postId.ToByteArray();
                var count = _database.ExecuteNonQuery(_delete1Command);
                return count;
            } // Lock
        }

        public DriveReactionsRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid driveId,OdinId identity,Guid postId,string singleReaction)
        {
            if (singleReaction == null) throw new Exception("Cannot be null");
            if (singleReaction?.Length < 3) throw new Exception("Too short");
            if (singleReaction?.Length > 80) throw new Exception("Too long");
            var result = new List<DriveReactionsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new DriveReactionsRecord();
            item.driveId = driveId;
            item.identity = identity;
            item.postId = postId;
            item.singleReaction = singleReaction;
            return item;
       }

        public DriveReactionsRecord Get(Guid driveId,OdinId identity,Guid postId,string singleReaction)
        {
            if (singleReaction == null) throw new Exception("Cannot be null");
            if (singleReaction?.Length < 3) throw new Exception("Too short");
            if (singleReaction?.Length > 80) throw new Exception("Too long");
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT driveId,identity,postId,singleReaction FROM driveReactions " +
                                                 "WHERE driveId = $driveId AND identity = $identity AND postId = $postId AND singleReaction = $singleReaction LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$driveId";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$identity";
                    _get0Param3 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param3);
                    _get0Param3.ParameterName = "$postId";
                    _get0Param4 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param4);
                    _get0Param4.ParameterName = "$singleReaction";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = driveId.ToByteArray();
                _get0Param2.Value = identity.DomainName;
                _get0Param3.Value = postId.ToByteArray();
                _get0Param4.Value = singleReaction;
                using (SqliteDataReader rdr = _database.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, driveId,identity,postId,singleReaction);
                    return r;
                } // using
            } // lock
        }

    }
}
