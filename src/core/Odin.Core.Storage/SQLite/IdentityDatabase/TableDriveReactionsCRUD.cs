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

        public TableDriveReactionsCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
        {
        }

        ~TableDriveReactionsCRUD()
        {
            if (_disposed == false) throw new Exception("TableDriveReactionsCRUD Not disposed properly");
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
                       cmd.CommandText = "DROP TABLE IF EXISTS driveReactions;";
                       conn.ExecuteNonQuery(cmd);
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
                    conn.ExecuteNonQuery(cmd);
            }
        }

        public virtual int Insert(DatabaseConnection conn, DriveReactionsRecord item)
        {
                using (var _insertCommand = _database.CreateCommand())
                {
                    _insertCommand.CommandText = "INSERT INTO driveReactions (driveId,identity,postId,singleReaction) " +
                                                 "VALUES ($driveId,$identity,$postId,$singleReaction)";
                    var _insertParam1 = _insertCommand.CreateParameter();
                    _insertParam1.ParameterName = "$driveId";
                    _insertCommand.Parameters.Add(_insertParam1);
                    var _insertParam2 = _insertCommand.CreateParameter();
                    _insertParam2.ParameterName = "$identity";
                    _insertCommand.Parameters.Add(_insertParam2);
                    var _insertParam3 = _insertCommand.CreateParameter();
                    _insertParam3.ParameterName = "$postId";
                    _insertCommand.Parameters.Add(_insertParam3);
                    var _insertParam4 = _insertCommand.CreateParameter();
                    _insertParam4.ParameterName = "$singleReaction";
                    _insertCommand.Parameters.Add(_insertParam4);
                _insertParam1.Value = item.driveId.ToByteArray();
                _insertParam2.Value = item.identity.DomainName;
                _insertParam3.Value = item.postId.ToByteArray();
                _insertParam4.Value = item.singleReaction;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                 {
                 }
                return count;
                } // Using
        }

        public virtual int Upsert(DatabaseConnection conn, DriveReactionsRecord item)
        {
                using (var _upsertCommand = _database.CreateCommand())
                {
                    _upsertCommand.CommandText = "INSERT INTO driveReactions (driveId,identity,postId,singleReaction) " +
                                                 "VALUES ($driveId,$identity,$postId,$singleReaction)"+
                                                 "ON CONFLICT (driveId,identity,postId,singleReaction) DO UPDATE "+
                                                 "SET  "+
                                                 ";";
                    var _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertParam1.ParameterName = "$driveId";
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    var _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertParam2.ParameterName = "$identity";
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    var _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertParam3.ParameterName = "$postId";
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    var _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertParam4.ParameterName = "$singleReaction";
                    _upsertCommand.Parameters.Add(_upsertParam4);
                _upsertParam1.Value = item.driveId.ToByteArray();
                _upsertParam2.Value = item.identity.DomainName;
                _upsertParam3.Value = item.postId.ToByteArray();
                _upsertParam4.Value = item.singleReaction;
                var count = conn.ExecuteNonQuery(_upsertCommand);
                return count;
                } // Using
        }
        public virtual int Update(DatabaseConnection conn, DriveReactionsRecord item)
        {
                using (var _updateCommand = _database.CreateCommand())
                {
                    _updateCommand.CommandText = "UPDATE driveReactions " +
                                                 "SET  "+
                                                 "WHERE (driveId = $driveId AND identity = $identity AND postId = $postId AND singleReaction = $singleReaction)";
                    var _updateParam1 = _updateCommand.CreateParameter();
                    _updateParam1.ParameterName = "$driveId";
                    _updateCommand.Parameters.Add(_updateParam1);
                    var _updateParam2 = _updateCommand.CreateParameter();
                    _updateParam2.ParameterName = "$identity";
                    _updateCommand.Parameters.Add(_updateParam2);
                    var _updateParam3 = _updateCommand.CreateParameter();
                    _updateParam3.ParameterName = "$postId";
                    _updateCommand.Parameters.Add(_updateParam3);
                    var _updateParam4 = _updateCommand.CreateParameter();
                    _updateParam4.ParameterName = "$singleReaction";
                    _updateCommand.Parameters.Add(_updateParam4);
                _updateParam1.Value = item.driveId.ToByteArray();
                _updateParam2.Value = item.identity.DomainName;
                _updateParam3.Value = item.postId.ToByteArray();
                _updateParam4.Value = item.singleReaction;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                }
                return count;
                } // Using
        }

        public virtual int GetCount(DatabaseConnection conn)
        {
                using (var _getCountCommand = _database.CreateCommand())
                {
                    _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveReactions; PRAGMA read_uncommitted = 0;";
                    var count = conn.ExecuteNonQuery(_getCountCommand);
                    return count;
                }
        }

        public virtual int GetDriveCount(DatabaseConnection conn, Guid driveId)
        {
                using (var _getCountDriveCommand = _database.CreateCommand())
                {
                    _getCountDriveCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveReactions WHERE driveId = $driveId;PRAGMA read_uncommitted = 0;";
                    var _getCountDriveParam1 = _getCountDriveCommand.CreateParameter();
                    _getCountDriveParam1.ParameterName = "$driveId";
                    _getCountDriveCommand.Parameters.Add(_getCountDriveParam1);
                    _getCountDriveParam1.Value = driveId.ToByteArray();
                    var count = conn.ExecuteNonQuery(_getCountDriveCommand);
                    return count;
                } // using
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

        public int Delete(DatabaseConnection conn, Guid driveId,OdinId identity,Guid postId,string singleReaction)
        {
            if (singleReaction == null) throw new Exception("Cannot be null");
            if (singleReaction?.Length < 3) throw new Exception("Too short");
            if (singleReaction?.Length > 80) throw new Exception("Too long");
                using (var _delete0Command = _database.CreateCommand())
                {
                    _delete0Command.CommandText = "DELETE FROM driveReactions " +
                                                 "WHERE driveId = $driveId AND identity = $identity AND postId = $postId AND singleReaction = $singleReaction";
                    var _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Param1.ParameterName = "$driveId";
                    _delete0Command.Parameters.Add(_delete0Param1);
                    var _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Param2.ParameterName = "$identity";
                    _delete0Command.Parameters.Add(_delete0Param2);
                    var _delete0Param3 = _delete0Command.CreateParameter();
                    _delete0Param3.ParameterName = "$postId";
                    _delete0Command.Parameters.Add(_delete0Param3);
                    var _delete0Param4 = _delete0Command.CreateParameter();
                    _delete0Param4.ParameterName = "$singleReaction";
                    _delete0Command.Parameters.Add(_delete0Param4);

                _delete0Param1.Value = driveId.ToByteArray();
                _delete0Param2.Value = identity.DomainName;
                _delete0Param3.Value = postId.ToByteArray();
                _delete0Param4.Value = singleReaction;
                var count = conn.ExecuteNonQuery(_delete0Command);
                return count;
                } // Using
        }

        public int DeleteAllReactions(DatabaseConnection conn, Guid driveId,OdinId identity,Guid postId)
        {
                using (var _delete1Command = _database.CreateCommand())
                {
                    _delete1Command.CommandText = "DELETE FROM driveReactions " +
                                                 "WHERE driveId = $driveId AND identity = $identity AND postId = $postId";
                    var _delete1Param1 = _delete1Command.CreateParameter();
                    _delete1Param1.ParameterName = "$driveId";
                    _delete1Command.Parameters.Add(_delete1Param1);
                    var _delete1Param2 = _delete1Command.CreateParameter();
                    _delete1Param2.ParameterName = "$identity";
                    _delete1Command.Parameters.Add(_delete1Param2);
                    var _delete1Param3 = _delete1Command.CreateParameter();
                    _delete1Param3.ParameterName = "$postId";
                    _delete1Command.Parameters.Add(_delete1Param3);

                _delete1Param1.Value = driveId.ToByteArray();
                _delete1Param2.Value = identity.DomainName;
                _delete1Param3.Value = postId.ToByteArray();
                var count = conn.ExecuteNonQuery(_delete1Command);
                return count;
                } // Using
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

        public DriveReactionsRecord Get(DatabaseConnection conn, Guid driveId,OdinId identity,Guid postId,string singleReaction)
        {
            if (singleReaction == null) throw new Exception("Cannot be null");
            if (singleReaction?.Length < 3) throw new Exception("Too short");
            if (singleReaction?.Length > 80) throw new Exception("Too long");
                using (var _get0Command = _database.CreateCommand())
                {
                    _get0Command.CommandText = "SELECT driveId,identity,postId,singleReaction FROM driveReactions " +
                                                 "WHERE driveId = $driveId AND identity = $identity AND postId = $postId AND singleReaction = $singleReaction LIMIT 1;";
                    var _get0Param1 = _get0Command.CreateParameter();
                    _get0Param1.ParameterName = "$driveId";
                    _get0Command.Parameters.Add(_get0Param1);
                    var _get0Param2 = _get0Command.CreateParameter();
                    _get0Param2.ParameterName = "$identity";
                    _get0Command.Parameters.Add(_get0Param2);
                    var _get0Param3 = _get0Command.CreateParameter();
                    _get0Param3.ParameterName = "$postId";
                    _get0Command.Parameters.Add(_get0Param3);
                    var _get0Param4 = _get0Command.CreateParameter();
                    _get0Param4.ParameterName = "$singleReaction";
                    _get0Command.Parameters.Add(_get0Param4);

                _get0Param1.Value = driveId.ToByteArray();
                _get0Param2.Value = identity.DomainName;
                _get0Param3.Value = postId.ToByteArray();
                _get0Param4.Value = singleReaction;
                    lock (conn._lock)
                    {
                using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, driveId,identity,postId,singleReaction);
                    return r;
                } // using
            } // lock
            } // using
        }

    }
}
